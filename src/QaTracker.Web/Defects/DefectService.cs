using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;

namespace QaTracker.Web.Defects;

/// <summary>Editable fields of a defect. Status and severity are set separately (via the
/// quick menus on the detail page), like a test result.</summary>
public sealed record DefectInput(
    string Summary,
    string? ReproSteps,
    string? ExpectedResults,
    string? ActualResults,
    string? AssignedToId);

/// <summary>A comment on a defect, with its author's display name resolved.</summary>
public sealed record DefectCommentView(Guid Id, string AuthorId, string AuthorName, string Body, DateTimeOffset CreatedUtc);

/// <summary>
/// Roll-up of a project's defects for the dashboard. <see cref="Total"/> excludes
/// defects dismissed as <see cref="DefectStatus.NotADefect"/> — by QA/dev agreement
/// those aren't bugs, so they shouldn't inflate the KPI. <see cref="Open"/> is a subset
/// of <see cref="Total"/> that further excludes <see cref="DefectStatus.Fixed"/>.
/// </summary>
public sealed record DefectSummary(int Total, int Open);

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
                Status = DefectStatus.NotFixed,
                AssignedToId = string.IsNullOrWhiteSpace(input.AssignedToId) ? null : input.AssignedToId,
                CreatedById = createdById,
                CreatedUtc = now,
                UpdatedUtc = now,
            };

            db.Defects.Add(defect);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                continue; // lost the race for this Number — recompute and retry
            }

            // First item in the project moves it from Inactive to Active.
            await projects.MarkInFlightAsync(projectId, ct);

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

        defect.Summary = input.Summary.Trim();
        defect.ReproSteps = Normalize(input.ReproSteps);
        defect.ExpectedResults = Normalize(input.ExpectedResults);
        defect.ActualResults = Normalize(input.ActualResults);
        defect.AssignedToId = newAssignedToId;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);

        if (newAssignedToId is not null && newAssignedToId != previousAssignedToId)
        {
            await notifications.NotifyAssignedAsync(defect, newAssignedToId, actingUserId, ct);
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
        defect.Status = status;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);

        if (status == previousStatus)
        {
            return;
        }

        if (status == DefectStatus.NotFixed)
        {
            await notifications.NotifyReturnedToNotFixedAsync(defect, actingUserId, ct);
        }
        else if (status == DefectStatus.ToCheck)
        {
            await notifications.NotifyReadyToCheckAsync(defect, actingUserId, ct);
        }
    }

    public async Task SetSeverityAsync(Guid id, DefectSeverity severity, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        defect.Severity = severity;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
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
        defect.AssignedToId = newAssignedToId;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);

        if (newAssignedToId is not null && newAssignedToId != previousAssignedToId)
        {
            await notifications.NotifyAssignedAsync(defect, newAssignedToId, actingUserId, ct);
        }
    }

    /// <summary>
    /// "Start fixing": a developer picks the defect up — it's assigned to them and moved to
    /// <see cref="DefectStatus.Fixing"/>. No notification (the dev is acting on their own defect).
    /// </summary>
    public async Task StartFixingAsync(Guid id, string devUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        defect.AssignedToId = devUserId;
        defect.Status = DefectStatus.Fixing;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
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

        defect.FixedById = devUserId;
        defect.AssignedToId = qaId; // null when the project has no QA on its team
        defect.Status = DefectStatus.ToCheck;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);

        if (qaId is not null)
        {
            await notifications.NotifyReadyToCheckAsync(defect, devUserId, ct);
        }

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

        defect.AssignedToId = defect.FixedById; // back to the alleged fixer (null if unknown)
        defect.Status = DefectStatus.NotFixed;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);

        await notifications.NotifyReturnedToNotFixedAsync(defect, qaUserId, ct);
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

        defect.TestedById = qaUserId;
        defect.AssignedToId = null;
        defect.Status = DefectStatus.Fixed;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

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

    /// <summary>Links a test case to the defect. Idempotent.</summary>
    public async Task LinkTestCaseAsync(Guid id, Guid testCaseId, CancellationToken ct = default)
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

        defect.TestCases.Add(testCase);
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes one test-case link from the defect. Idempotent.</summary>
    public async Task UnlinkTestCaseAsync(Guid id, Guid testCaseId, CancellationToken ct = default)
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

        defect.TestCases.Remove(link);
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await attachments.PurgeForDefectAsync(id, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Defects.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<DefectCommentView>> ListCommentsAsync(Guid defectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var comments = await db.DefectComments
            .AsNoTracking()
            .Where(c => c.DefectId == defectId)
            .Include(c => c.Author)
            .ToListAsync(ct);

        return comments
            .OrderBy(c => c.CreatedUtc)
            .Select(c => new DefectCommentView(c.Id, c.AuthorId, DisplayName(c.Author), c.Body.Trim(), c.CreatedUtc))
            .ToList();
    }

    public async Task<Guid> AddCommentAsync(Guid defectId, string authorId, string body, CancellationToken ct = default)
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
            await notifications.NotifyCommentAsync(defect, authorId, ct);
        }

        return comment.Id;
    }

    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default)
    {
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
