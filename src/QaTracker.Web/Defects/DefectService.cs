using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Defects;

/// <summary>Editable fields of a defect. Status and severity are set separately (via the
/// quick menus on the detail page), like a test result.</summary>
public sealed record DefectInput(
    string Summary,
    string? ReproSteps,
    string? ExpectedResults,
    string? ActualResults,
    string? AssignedToId,
    DefectTemplate Template = DefectTemplates.SystemFallback);

/// <summary>
/// Stepping through the defects assigned to someone that still need work from them (Not fixed,
/// Fixing, To check), in the defects page's order (most severe first, then number) — the defect
/// page's Previous / Next. <see cref="Position"/> is the current defect's 1-based place in that
/// queue, or null when it isn't in it (e.g. it's just been handed on); Previous / Next then step to
/// the nearest queued defect either side of it in the page order.
/// </summary>
public sealed record DefectNavigation(
    Defect? Previous, Defect? Next, int Count, int? Position, int NotFixed, int Fixing, int ToCheck)
{
    public static readonly DefectNavigation Empty = new(null, null, 0, null, 0, 0, 0);

    /// <summary>The statuses that put a defect in someone's queue.</summary>
    public static bool NeedsWork(DefectStatus status) =>
        status is DefectStatus.NotFixed or DefectStatus.Fixing or DefectStatus.ToCheck;
}

/// <summary>A comment on a defect, with its author's display name resolved.</summary>
public sealed record DefectCommentView(
    Guid Id, string AuthorId, string AuthorName, string Body, DateTimeOffset CreatedUtc,
    IReadOnlyList<CommentAttachmentView> Attachments, IReadOnlyList<CommentReactionView> Reactions);

/// <summary>
/// Roll-up of a project's defects for the dashboard. <see cref="Total"/> excludes
/// defects dismissed as <see cref="DefectStatus.NotADefect"/> — by QA/dev agreement
/// those aren't bugs, so they shouldn't inflate the KPI. <see cref="Open"/> is a subset
/// of <see cref="Total"/> that further excludes <see cref="DefectStatus.Fixed"/>.
/// </summary>
public sealed record DefectSummary(int Total, int Open);

/// <summary>
/// Where a project's outstanding defects stand, for the All Projects list: <see cref="NotFixed"/>
/// and <see cref="Fixing"/> make up <see cref="Open"/> (dev work left); <see cref="ToCheck"/> is
/// waiting on QA to verify the fix.
/// </summary>
public sealed record ProjectDefectCounts(int NotFixed, int Fixing, int ToCheck)
{
    public static readonly ProjectDefectCounts None = new(0, 0, 0);

    public int Open => NotFixed + Fixing;
}

/// <summary>
/// Reads and writes <see cref="Defect"/> aggregates. Uses a context factory so each call
/// gets a short-lived context (safe under Blazor Server circuits). Mirrors
/// <see cref="TestCases.TestCaseService"/>.
/// </summary>
public sealed class DefectService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider timeProvider,
    ProjectService projects,
    AttachmentService attachments,
    NotificationService notifications)
{
    /// <summary>Defects in a project, most severe first then by number. Includes linked test cases.</summary>
    public async Task<IReadOnlyList<Defect>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.Defects
            .AsNoTracking()
            .Include(d => d.AssignedTo)
            .Include(d => d.TestCases)
            .Where(d => d.ProjectId == projectId)
            .ToListAsync(ct);

        return Ordered(list);
    }

    /// <summary>Previous / Next through <paramref name="userId"/>'s assigned defects that still need
    /// work (see <see cref="DefectNavigation"/>).</summary>
    public async Task<DefectNavigation> GetAssignedNavigationAsync(
        Guid projectId, Guid defectId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return DefectNavigation.Empty;
        }

        var all = (await ListForProjectAsync(projectId, ct)).ToList();
        var index = all.FindIndex(d => d.Id == defectId);
        if (index < 0)
        {
            return DefectNavigation.Empty;
        }

        bool Queued(Defect d) => d.AssignedToId == userId && DefectNavigation.NeedsWork(d.Status);
        var queue = all.Where(Queued).ToList();
        var position = queue.FindIndex(d => d.Id == defectId);

        return new DefectNavigation(
            Previous: all.Take(index).LastOrDefault(Queued),
            Next: all.Skip(index + 1).FirstOrDefault(Queued),
            Count: queue.Count,
            Position: position < 0 ? null : position + 1,
            NotFixed: queue.Count(d => d.Status == DefectStatus.NotFixed),
            Fixing: queue.Count(d => d.Status == DefectStatus.Fixing),
            ToCheck: queue.Count(d => d.Status == DefectStatus.ToCheck));
    }

    /// <summary>A project's defects still to be resolved — anything not Fixed or dismissed as
    /// Not a defect (the same rule as <see cref="DefectSummary.Open"/>) — most severe first.</summary>
    public async Task<IReadOnlyList<Defect>> ListOpenForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.Defects
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId
                && d.Status != DefectStatus.Fixed
                && d.Status != DefectStatus.NotADefect)
            .ToListAsync(ct);

        return Ordered(list);
    }

    /// <summary>Defects linked to a given test case, most severe first then by number.</summary>
    public async Task<IReadOnlyList<Defect>> ListForTestCaseAsync(Guid testCaseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.Defects
            .AsNoTracking()
            .Where(d => d.TestCases.Any(tc => tc.Id == testCaseId))
            .ToListAsync(ct);

        return Ordered(list);
    }

    /// <summary>Most severe first, then ascending by per-project number.</summary>
    private static IReadOnlyList<Defect> Ordered(IEnumerable<Defect> defects) =>
        defects
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Number)
            .ToList();

    public async Task<Defect?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects
            .AsNoTracking()
            .Include(d => d.AssignedTo)
            .Include(d => d.FixedBy)
            .Include(d => d.TestedBy)
            .Include(d => d.TestCases)
                .ThenInclude(tc => tc.TestScope)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (defect is not null)
        {
            defect.TestCases.Sort((a, b) => string.Compare(a.Scenario, b.Scenario, StringComparison.OrdinalIgnoreCase));
        }

        return defect;
    }

    public async Task<Defect> CreateAsync(
        Guid projectId,
        DefectInput input,
        string createdById,
        DefectSeverity severity = DefectSeverity.Medium,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var maxNumber = await db.Defects
                .Where(d => d.ProjectId == projectId)
                .MaxAsync(d => (int?)d.Number, ct) ?? 0;

            var defect = new Defect
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Number = maxNumber + 1,
                Summary = input.Summary.Trim(),
                ReproSteps = Normalize(input.ReproSteps),
                ExpectedResults = Normalize(input.ExpectedResults),
                ActualResults = Normalize(input.ActualResults),
                Severity = Enum.IsDefined(severity) ? severity : DefectSeverity.Medium,
                Template = Enum.IsDefined(input.Template) ? input.Template : DefectTemplates.SystemFallback,
                Status = DefectStatus.NotFixed,
                AssignedToId = string.IsNullOrWhiteSpace(input.AssignedToId) ? null : input.AssignedToId,
                CreatedById = createdById,
                CreatedUtc = now,
                UpdatedUtc = now,
            };

            db.Defects.Add(defect);
            DefectHistory.Record(db, defect.Id, DefectEventKind.Created, createdById, now);
            if (defect.AssignedToId is not null)
            {
                DefectHistory.Record(db, defect.Id, DefectEventKind.AssigneeChanged, createdById, now,
                    toValue: await NameOfAsync(db, defect.AssignedToId, ct));
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                continue; // lost the race for this Number — recompute and retry
            }

            // First item in the project moves it from Not Started to Active.
            await projects.MarkInFlightAsync(projectId, createdById, ct);

            await notifications.NotifyNewDefectAsync(defect, createdById, ct);
            if (defect.AssignedToId is not null)
            {
                await notifications.NotifyAssignedAsync(defect, defect.AssignedToId, createdById, ct);
            }

            return defect;
        }

        throw new InvalidOperationException($"Could not allocate a defect number for project {projectId}.");
    }

    /// <summary>Updates the editable fields. Does not touch status or severity.
    /// <paramref name="actingUserId"/> is the editor — they aren't notified if they assign
    /// the defect to themselves.</summary>
    public async Task UpdateAsync(Guid id, DefectInput input, string? actingUserId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var previousAssignedToId = defect.AssignedToId;
        var newAssignedToId = string.IsNullOrWhiteSpace(input.AssignedToId) ? null : input.AssignedToId;
        var now = timeProvider.GetUtcNow();

        var changed = new List<string>();
        void Set(string? current, string? next, Action<string?> apply, string field)
        {
            if (!string.Equals(current, next, StringComparison.Ordinal))
            {
                changed.Add(field);
            }
            apply(next);
        }

        Set(defect.Summary, input.Summary.Trim(), v => defect.Summary = v!, "summary");
        Set(defect.ReproSteps, Normalize(input.ReproSteps), v => defect.ReproSteps = v, "repro steps");
        Set(defect.ExpectedResults, Normalize(input.ExpectedResults), v => defect.ExpectedResults = v, "expected results");
        Set(defect.ActualResults, Normalize(input.ActualResults), v => defect.ActualResults = v, "actual results");
        defect.UpdatedUtc = now;

        var editedWhat = DefectHistory.DescribeEditedFields(changed);
        if (editedWhat is not null)
        {
            DefectHistory.Record(db, id, DefectEventKind.Edited, actingUserId, now, toValue: editedWhat);
        }

        if (Enum.IsDefined(input.Template) && defect.Template != input.Template)
        {
            DefectHistory.Record(db, id, DefectEventKind.TemplateChanged, actingUserId, now,
                defect.Template.ToString(), input.Template.ToString());
            defect.Template = input.Template;
        }

        await RecordAssigneeAsync(db, defect, newAssignedToId, actingUserId, now, ct);
        defect.AssignedToId = newAssignedToId;

        await db.SaveChangesAsync(ct);

        await NotifyAssigneeChangeAsync(defect, previousAssignedToId, actingUserId, ct);
        // An assignee who stays on it hears about the edit (a new one gets the assignment instead).
        if (editedWhat is not null && newAssignedToId == previousAssignedToId)
        {
            await notifications.NotifyEditedAsync(defect, editedWhat, actingUserId, ct);
        }
    }

    /// <summary>After an assignee change: the new assignee is told it's theirs, the previous one
    /// that it isn't any more (neither when they made the change themselves).</summary>
    private async Task NotifyAssigneeChangeAsync(Defect defect, string? previousAssignedToId, string? actingUserId, CancellationToken ct)
    {
        if (defect.AssignedToId == previousAssignedToId)
        {
            return;
        }

        if (defect.AssignedToId is not null)
        {
            await notifications.NotifyAssignedAsync(defect, defect.AssignedToId, actingUserId, ct);
        }

        if (previousAssignedToId is not null)
        {
            await notifications.NotifyUnassignedAsync(defect, previousAssignedToId, actingUserId, ct);
        }
    }

    /// <summary><paramref name="actingUserId"/> is whoever changed the status — the assignee
    /// isn't notified about a status change they made themselves.</summary>
    public async Task SetStatusAsync(Guid id, DefectStatus status, string? actingUserId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var previousStatus = defect.Status;
        var now = timeProvider.GetUtcNow();
        RecordStatus(db, defect, status, actingUserId, now);
        defect.Status = status;
        defect.UpdatedUtc = now;

        await db.SaveChangesAsync(ct);

        if (status != previousStatus)
        {
            await notifications.NotifyStatusChangedAsync(defect, actingUserId, ct);
        }
    }

    public async Task SetSeverityAsync(Guid id, DefectSeverity severity, string? actingUserId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var now = timeProvider.GetUtcNow();
        var changed = defect.Severity != severity;
        if (changed)
        {
            DefectHistory.Record(db, id, DefectEventKind.SeverityChanged, actingUserId, now,
                defect.Severity.ToString(), severity.ToString());
        }

        defect.Severity = severity;
        defect.UpdatedUtc = now;

        await db.SaveChangesAsync(ct);

        if (changed)
        {
            await notifications.NotifyEditedAsync(defect, $"severity now {DefectDisplay.SeverityLabel(severity)}", actingUserId, ct);
        }
    }

    /// <summary><paramref name="actingUserId"/> is whoever made the change — no notification
    /// when someone assigns a defect to themselves.</summary>
    public async Task SetAssigneeAsync(Guid id, string? assigneeId, string? actingUserId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var previousAssignedToId = defect.AssignedToId;
        var newAssignedToId = string.IsNullOrWhiteSpace(assigneeId) ? null : assigneeId;
        var now = timeProvider.GetUtcNow();
        await RecordAssigneeAsync(db, defect, newAssignedToId, actingUserId, now, ct);
        defect.AssignedToId = newAssignedToId;
        defect.UpdatedUtc = now;

        await db.SaveChangesAsync(ct);

        await NotifyAssigneeChangeAsync(defect, previousAssignedToId, actingUserId, ct);
    }

    /// <summary>
    /// "Start fixing": a developer picks the defect up — it's assigned to them and moved to
    /// <see cref="DefectStatus.Fixing"/>. Whoever it was assigned to before is told it's been taken over.
    /// </summary>
    public async Task StartFixingAsync(Guid id, string devUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var previousAssignedToId = defect.AssignedToId;
        var now = timeProvider.GetUtcNow();
        RecordStatus(db, defect, DefectStatus.Fixing, devUserId, now);
        await RecordAssigneeAsync(db, defect, devUserId, devUserId, now, ct);
        defect.AssignedToId = devUserId;
        defect.Status = DefectStatus.Fixing;
        defect.UpdatedUtc = now;

        await db.SaveChangesAsync(ct);

        // Whoever had it before (if not this dev) hears it's been taken over.
        if (previousAssignedToId is not null)
        {
            await notifications.NotifyUnassignedAsync(defect, previousAssignedToId, devUserId, ct);
        }
    }

    /// <summary>
    /// "Mark as fixed": records <paramref name="devUserId"/> as <see cref="Defect.FixedById"/>,
    /// moves the defect to the validation state (<see cref="DefectStatus.ToCheck"/>), and hands
    /// it to a random QA on the project team — or leaves it unassigned when the project has no
    /// QAs. The QA it lands on is notified that it's ready to check.
    /// </summary>
    /// <returns>The id of the QA it was assigned to, or <c>null</c> if it was left unassigned.</returns>
    public async Task<string?> MarkFixedAsync(Guid id, string devUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var qaId = await PickRandomProjectQaAsync(db, defect.ProjectId, ct);

        var now = timeProvider.GetUtcNow();
        RecordStatus(db, defect, DefectStatus.ToCheck, devUserId, now);
        await RecordAssigneeAsync(db, defect, qaId, devUserId, now, ct);
        defect.FixedById = devUserId;
        defect.AssignedToId = qaId; // null when the project has no QA on its team
        defect.Status = DefectStatus.ToCheck;
        defect.UpdatedUtc = now;

        await db.SaveChangesAsync(ct);

        // The QA it lands on hears it's ready to check (one notification, not "assigned" as well).
        await notifications.NotifyStatusChangedAsync(defect, devUserId, ct);

        return qaId;
    }

    /// <summary>
    /// QA rejects a fix ("Not fixed" button): sends the defect back to the developer who
    /// marked it fixed (<see cref="Defect.FixedById"/>), status → <see cref="DefectStatus.NotFixed"/>.
    /// That developer is alerted.
    /// </summary>
    public async Task RejectFixAsync(Guid id, string qaUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var now = timeProvider.GetUtcNow();
        RecordStatus(db, defect, DefectStatus.NotFixed, qaUserId, now);
        await RecordAssigneeAsync(db, defect, defect.FixedById, qaUserId, now, ct);
        defect.AssignedToId = defect.FixedById; // back to the alleged fixer (null if unknown)
        defect.Status = DefectStatus.NotFixed;
        defect.UpdatedUtc = now;

        await db.SaveChangesAsync(ct);

        await notifications.NotifyStatusChangedAsync(defect, qaUserId, ct);
    }

    /// <summary>
    /// QA confirms a fix ("Fixed" button): status → <see cref="DefectStatus.Fixed"/>, records
    /// <paramref name="qaUserId"/> as <see cref="Defect.TestedById"/>, and unassigns the defect
    /// (the work is done).
    /// </summary>
    public async Task VerifyFixedAsync(Guid id, string qaUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var now = timeProvider.GetUtcNow();
        RecordStatus(db, defect, DefectStatus.Fixed, qaUserId, now);
        await RecordAssigneeAsync(db, defect, null, qaUserId, now, ct);
        defect.TestedById = qaUserId;
        defect.AssignedToId = null;
        defect.Status = DefectStatus.Fixed;
        defect.UpdatedUtc = now;

        await db.SaveChangesAsync(ct);
    }

    private static async Task<string?> PickRandomProjectQaAsync(
        ApplicationDbContext db, Guid projectId, CancellationToken ct)
    {
        var qaIds = await (
            from p in db.Projects.Where(p => p.Id == projectId)
            from m in p.Members
            join ur in db.UserRoles on m.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where r.Name == Roles.QA
            select m.Id).ToListAsync(ct);

        return qaIds.Count == 0 ? null : qaIds[Random.Shared.Next(qaIds.Count)];
    }

    /// <summary>Links a test case to the defect. Idempotent. Recorded in both histories as
    /// <paramref name="actingUserId"/>.</summary>
    public async Task LinkTestCaseAsync(Guid id, Guid testCaseId, string? actingUserId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects
            .Include(d => d.TestCases)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        if (defect.TestCases.Any(tc => tc.Id == testCaseId))
        {
            return;
        }

        var testCase = await db.TestCases.FirstOrDefaultAsync(tc => tc.Id == testCaseId, ct)
            ?? throw new InvalidOperationException($"Test case {testCaseId} not found.");

        var now = timeProvider.GetUtcNow();
        defect.TestCases.Add(testCase);
        defect.UpdatedUtc = now;
        DefectHistory.Record(db, id, DefectEventKind.TestCaseLinked, actingUserId, now, toValue: testCase.Scenario);
        TestCaseHistory.Record(db, testCaseId, TestCaseEventKind.DefectLinked, actingUserId, now, toValue: DefectLabel(defect));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes one test-case link from the defect. Idempotent. Recorded in both
    /// histories as <paramref name="actingUserId"/>.</summary>
    public async Task UnlinkTestCaseAsync(Guid id, Guid testCaseId, string? actingUserId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects
            .Include(d => d.TestCases)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var link = defect.TestCases.FirstOrDefault(tc => tc.Id == testCaseId);
        if (link is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        defect.TestCases.Remove(link);
        defect.UpdatedUtc = now;
        DefectHistory.Record(db, id, DefectEventKind.TestCaseUnlinked, actingUserId, now, fromValue: link.Scenario);
        TestCaseHistory.Record(db, testCaseId, TestCaseEventKind.DefectUnlinked, actingUserId, now, fromValue: DefectLabel(defect));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The defect's history, newest first.</summary>
    /// <summary>
    /// Fingerprint of everything the defect page shows (<see cref="LiveVersion"/>): the defect, its
    /// history (files and test case links add entries), its comments and their reactions, its
    /// linked test cases as they are now, the project's team (the assignee picker) and the
    /// project's defect workflow — every status / assignee change anywhere moves the page's
    /// Previous / Next. Null when the defect is gone or isn't in <paramref name="projectId"/>.
    /// </summary>
    public async Task<string?> GetLiveVersionAsync(Guid projectId, Guid defectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var d = await db.Defects.AsNoTracking()
            .Where(x => x.Id == defectId && x.ProjectId == projectId)
            .Select(x => new
            {
                x.Summary, x.ReproSteps, x.ExpectedResults, x.ActualResults, x.Severity, x.Template,
                x.Status, x.AssignedToId, x.FixedById, x.TestedById,
            })
            .FirstOrDefaultAsync(ct);
        if (d is null)
        {
            return null;
        }

        var events = await db.DefectEvents.CountAsync(e => e.DefectId == defectId, ct);
        var comments = (await db.DefectComments.Where(c => c.DefectId == defectId).Select(c => c.Id).ToListAsync(ct))
            .Order().ToList();
        var reactions = (await db.CommentReactions
                .Where(r => r.DefectComment!.DefectId == defectId)
                .Select(r => new { r.DefectCommentId, r.UserId, r.Reaction })
                .ToListAsync(ct))
            .Select(r => $"{r.DefectCommentId}:{r.UserId}:{r.Reaction}").Order().ToList();
        var cases = (await db.TestCases
                .Where(t => t.Defects.Any(x => x.Id == defectId))
                .Select(t => new { t.Id, t.Result, t.Scenario, t.TestScopeId })
                .ToListAsync(ct))
            .Select(t => $"{t.Id}:{t.Result}:{t.TestScopeId}:{t.Scenario}").Order().ToList();
        var team = (await db.Projects.Where(p => p.Id == projectId).SelectMany(p => p.Members.Select(m => m.Id)).ToListAsync(ct))
            .Order().ToList();
        var workflow = await db.DefectEvents.CountAsync(
            e => e.Defect!.ProjectId == projectId
                && (e.Kind == DefectEventKind.StatusChanged || e.Kind == DefectEventKind.AssigneeChanged), ct);
        var projectDefects = await db.Defects.CountAsync(x => x.ProjectId == projectId, ct);
        // New cases show up in the "Link test cases" picker.
        var projectCases = await db.TestCases.CountAsync(t => t.TestScope!.ProjectId == projectId, ct);

        return LiveVersion.Of(d.Summary, d.ReproSteps, d.ExpectedResults, d.ActualResults, d.Severity, d.Template,
            d.Status, d.AssignedToId, d.FixedById, d.TestedById,
            events, comments, reactions, cases, team, workflow, projectDefects, projectCases);
    }

    /// <summary>
    /// Fingerprint of a project's defects list (<see cref="LiveVersion"/>): every create, edit,
    /// status, severity, assignee and link change adds a defect history entry, so counting those
    /// (with the defects, for deletions) catches them without reading every row.
    /// </summary>
    public async Task<string> GetListLiveVersionAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defects = await db.Defects.CountAsync(x => x.ProjectId == projectId, ct);
        var events = await db.DefectEvents.CountAsync(e => e.Defect!.ProjectId == projectId, ct);
        return LiveVersion.Of(defects, events);
    }

    public async Task<IReadOnlyList<DefectEventView>> ListHistoryAsync(Guid defectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var events = await db.DefectEvents
            .AsNoTracking()
            .Where(e => e.DefectId == defectId)
            .Include(e => e.Actor)
            .ToListAsync(ct);

        // In memory: SQLite (used in tests) can't ORDER BY DateTimeOffset.
        return events
            .OrderByDescending(e => e.OccurredUtc)
            .ThenBy(e => DefectHistory.SameInstantRank(e.Kind))
            .Select(e => new DefectEventView(
                e.Id, e.Kind, e.ActorId, DisplayName(e.Actor), e.OccurredUtc, e.FromValue, e.ToValue))
            .ToList();
    }

    private static void RecordStatus(ApplicationDbContext db, Defect defect, DefectStatus next, string? actorId, DateTimeOffset now)
    {
        if (defect.Status != next)
        {
            DefectHistory.Record(db, defect.Id, DefectEventKind.StatusChanged, actorId, now,
                defect.Status.ToString(), next.ToString());
        }
    }

    /// <summary>Records an assignee change (call before applying it), with both names as they are now.</summary>
    private static async Task RecordAssigneeAsync(
        ApplicationDbContext db, Defect defect, string? nextAssigneeId, string? actorId, DateTimeOffset now, CancellationToken ct)
    {
        if (defect.AssignedToId == nextAssigneeId)
        {
            return;
        }

        DefectHistory.Record(db, defect.Id, DefectEventKind.AssigneeChanged, actorId, now,
            await NameOfAsync(db, defect.AssignedToId, ct), await NameOfAsync(db, nextAssigneeId, ct));
    }

    private static async Task<string?> NameOfAsync(ApplicationDbContext db, string? userId, CancellationToken ct)
    {
        if (userId is null)
        {
            return null;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return DisplayName(user);
    }

    private static string DefectLabel(Defect defect) => $"{DefectDisplay.Ref(defect.Number)} {defect.Summary}";

    /// <summary>Deletes a defect (its own history goes with it), recording the deletion in the
    /// project's history as <paramref name="actorId"/>.</summary>
    public async Task DeleteAsync(Guid id, string? actorId = null, CancellationToken ct = default)
    {
        await attachments.PurgeForDefectAsync(id, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (defect is null)
        {
            return;
        }

        ProjectHistory.Record(db, defect.ProjectId, ProjectEventKind.DefectDeleted, actorId,
            timeProvider.GetUtcNow(), fromValue: DefectLabel(defect));
        await db.SaveChangesAsync(ct);
        await db.Defects.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<DefectCommentView>> ListCommentsAsync(Guid defectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var comments = await db.DefectComments
            .AsNoTracking()
            .Where(c => c.DefectId == defectId)
            .Include(c => c.Author)
            .Include(c => c.Attachments)
            .Include(c => c.Reactions).ThenInclude(r => r.User)
            .AsSplitQuery()
            .ToListAsync(ct);

        return comments
            .OrderBy(c => c.CreatedUtc)
            .Select(c => new DefectCommentView(
                c.Id, c.AuthorId, DisplayName(c.Author), c.Body.Trim(), c.CreatedUtc,
                c.Attachments.OrderBy(a => a.SortOrder).Select(CommentAttachmentView.From).ToList(),
                CommentReactionView.ListFrom(c.Reactions)))
            .ToList();
    }

    public async Task<Guid> AddCommentAsync(Guid defectId, string authorId, string body,
        IReadOnlyCollection<string>? mentionedUserIds = null, CancellationToken ct = default)
    {
        var comment = new DefectComment
        {
            Id = Guid.NewGuid(),
            DefectId = defectId,
            AuthorId = authorId,
            Body = body.Trim(),
            CreatedUtc = timeProvider.GetUtcNow(),
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.DefectComments.Add(comment);
        await db.SaveChangesAsync(ct);

        var defect = await db.Defects.AsNoTracking().FirstOrDefaultAsync(d => d.Id == defectId, ct);
        if (defect is not null)
        {
            var mentioned = await notifications.NotifyMentionedAsync(
                defect.ProjectId, defect, null, authorId, comment.Body, mentionedUserIds, ct);
            await notifications.NotifyCommentAsync(defect, authorId, mentioned, ct);
        }

        return comment.Id;
    }

    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default)
    {
        await attachments.PurgeForCommentAsync(AttachmentOwner.DefectComment, commentId, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.DefectComments.Where(c => c.Id == commentId).ExecuteDeleteAsync(ct);
    }

    public async Task<DefectSummary> SummariseProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var total = await db.Defects.CountAsync(
            d => d.ProjectId == projectId && d.Status != DefectStatus.NotADefect, ct);
        var open = await db.Defects.CountAsync(
            d => d.ProjectId == projectId
                && d.Status != DefectStatus.Fixed
                && d.Status != DefectStatus.NotADefect, ct);

        return new DefectSummary(total, open);
    }

    /// <summary>
    /// <see cref="ProjectDefectCounts"/> for several projects in one grouped query (the All
    /// Projects list). Every requested id gets an entry — <see cref="ProjectDefectCounts.None"/>
    /// when it has no outstanding defects.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ProjectDefectCounts>> CountOutstandingByProjectAsync(
        IEnumerable<Guid> projectIds, CancellationToken ct = default)
    {
        var ids = projectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ProjectDefectCounts>();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Defects
            .Where(d => ids.Contains(d.ProjectId)
                && (d.Status == DefectStatus.NotFixed || d.Status == DefectStatus.Fixing || d.Status == DefectStatus.ToCheck))
            .GroupBy(d => new { d.ProjectId, d.Status })
            .Select(g => new { g.Key.ProjectId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return ids.ToDictionary(id => id, id =>
        {
            int Count(DefectStatus s) => rows.FirstOrDefault(x => x.ProjectId == id && x.Status == s)?.Count ?? 0;
            return new ProjectDefectCounts(Count(DefectStatus.NotFixed), Count(DefectStatus.Fixing), Count(DefectStatus.ToCheck));
        });
    }

    private static string DisplayName(ApplicationUser? user)
    {
        if (user is null)
        {
            return "Unknown";
        }

        return !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.UserName ?? "Unknown";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
