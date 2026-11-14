using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Admin;

/// <summary>What one user did, either everywhere or in one project — the sums the admin
/// User activity page shows as its stat tiles and per-project rows.</summary>
/// <param name="ResultsRecorded">Results recorded (passed / failed / blocked / inconclusive).</param>
/// <param name="CasesTested">Distinct test cases those results were on.</param>
/// <param name="DefectsFixed">Distinct defects they marked as fixed.</param>
/// <param name="FixesVerified">Distinct defects they confirmed fixed.</param>
/// <param name="FixesSentBack">Fixes they rejected (each rejection counts).</param>
public sealed record UserActivityCounts(
    int ResultsRecorded,
    int CasesTested,
    int DefectsRaised,
    int DefectsFixed,
    int FixesVerified,
    int FixesSentBack,
    int Comments,
    int Files,
    long FileBytes,
    bool HasActivity,
    DateTimeOffset? LastActiveUtc);

/// <summary>A project the user is on the team of or did something in, with what they did there.</summary>
public sealed record UserActivityProject(Guid Id, string Name, ProjectStatus Status, bool IsMember, UserActivityCounts Counts);

/// <summary>A result the user recorded (or a run they started / reset) on a test case.</summary>
public sealed record UserTestRun(
    Guid EventId, Guid ProjectId, Guid TestCaseId, int Number, string Scenario,
    TestResult? From, TestResult To, DateTimeOffset OccurredUtc);

/// <summary>A defect the user raised, with where it is now.</summary>
public sealed record UserDefectRaised(
    Guid DefectId, Guid ProjectId, int Number, string Summary,
    DefectStatus Status, DefectSeverity Severity, DateTimeOffset RaisedUtc);

/// <summary>What the user did to move a defect through the fix workflow.</summary>
public enum DefectWorkKind
{
    /// <summary>Marked fixed (sent to QA to check).</summary>
    Fixed,

    /// <summary>Confirmed a fix (closed as Fixed).</summary>
    Verified,

    /// <summary>Rejected a fix — sent it back from To check.</summary>
    SentBack,

    /// <summary>Closed it as Not a defect.</summary>
    NotADefect,
}

/// <summary>One step of defect work. <paramref name="OccurredUtc"/> is null for fixes and
/// verifications from before defect history was recorded (known only from the defect's
/// fixed-by / tested-by fields).</summary>
public sealed record UserDefectWork(
    Guid DefectId, Guid ProjectId, int Number, string Summary, DefectStatus CurrentStatus,
    DefectWorkKind Kind, DateTimeOffset? OccurredUtc);

/// <summary>Where a comment or file sits: the project dashboard, a test case or a defect.</summary>
public enum UserActivityTarget
{
    Project,
    TestCase,
    Defect,
}

/// <summary>A comment the user posted. <paramref name="ThreadPage"/> is the comment thread page
/// it's on (comments page oldest first, <see cref="CommentPaging.PageSize"/> a page).</summary>
public sealed record UserComment(
    Guid CommentId, Guid ProjectId, UserActivityTarget Target, Guid? TargetId, int TargetNumber,
    string TargetTitle, string Body, DateTimeOffset CreatedUtc, int ThreadPage, int Files);

/// <summary>A file the user uploaded — to an item's Attachments, or with a comment.</summary>
public sealed record UserUpload(
    Guid AttachmentId, Guid ProjectId, UserActivityTarget Target, Guid? TargetId, int TargetNumber,
    string TargetTitle, bool OnComment, string FileName, string ContentType, long SizeBytes,
    DateTimeOffset UploadedUtc);

/// <summary>One row of the user's timeline: an entry from a project / test case / defect history
/// (as the project activity feed models them), or a comment they posted.</summary>
public sealed record UserTimelineItem(Guid ProjectId, ProjectActivityItem? History, UserComment? Comment)
{
    public DateTimeOffset OccurredUtc => History?.OccurredUtc ?? Comment!.CreatedUtc;

    public Guid Id => History?.Id ?? Comment!.CommentId;

    public int SameInstantRank => History?.SameInstantRank ?? 5;
}

/// <summary>Everything the User activity page shows, for one user in one scope (every project,
/// or one). <see cref="Projects"/> is always every project the user has touched or is on the
/// team of, so the page can offer them as scopes.</summary>
public sealed record UserActivityReport(
    IReadOnlyList<UserActivityProject> Projects,
    Guid? ProjectId,
    UserActivityCounts Totals,
    IReadOnlyDictionary<TestResult, int> ResultTally,
    IReadOnlyList<UserTestRun> TestRuns,
    IReadOnlyList<UserDefectRaised> DefectsRaised,
    IReadOnlyList<UserDefectWork> DefectWork,
    IReadOnlyList<UserComment> Comments,
    IReadOnlyList<UserUpload> Uploads,
    IReadOnlyList<UserTimelineItem> Timeline)
{
    public string ProjectName(Guid projectId) =>
        Projects.FirstOrDefault(p => p.Id == projectId)?.Name ?? "Unknown project";
}

/// <summary>
/// A user's activity for the System settings user pages, built from what the app already
/// records: test case / defect / project history (<see cref="TestCaseEvent"/>,
/// <see cref="DefectEvent"/>, <see cref="ProjectEvent"/>), comments, uploads, and the
/// fixed-by / tested-by fields on defects (which predate defect history).
///
/// <para>One user's rows are loaded and summarised in memory: an admin page opened now and then,
/// over a single user's work, so a handful of indexed per-user queries is cheap — and it keeps
/// the per-project sums and the drill-down lists consistent with each other.</para>
/// </summary>
public sealed class UserActivityService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    /// <summary>Results that count as "recorded" — a verdict, not starting or resetting a run.</summary>
    public static readonly IReadOnlyList<TestResult> RecordedResults =
        [TestResult.Passed, TestResult.Failed, TestResult.Blocked, TestResult.Inconclusive];

    public async Task<UserActivityReport> GetAsync(string userId, Guid? projectId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var caseEvents = await db.TestCaseEvents.AsNoTracking()
            .Where(e => e.ActorId == userId && e.Kind != TestCaseEventKind.Imported)
            .Select(e => new
            {
                e.Id, e.TestCaseId, e.Kind, e.OccurredUtc, e.FromValue, e.ToValue,
                e.TestCase!.ProjectId, e.TestCase.Number, e.TestCase.Scenario,
            })
            .ToListAsync(ct);

        var defectEvents = await db.DefectEvents.AsNoTracking()
            .Where(e => e.ActorId == userId)
            .Select(e => new
            {
                e.Id, e.DefectId, e.Kind, e.OccurredUtc, e.FromValue, e.ToValue,
                e.Defect!.ProjectId, e.Defect.Number, e.Defect.Summary, e.Defect.Status,
            })
            .ToListAsync(ct);

        var projectEvents = await db.ProjectEvents.AsNoTracking()
            .Where(e => e.ActorId == userId)
            .ToListAsync(ct);

        // Defects they raised, and ones whose fixed-by / tested-by names them (fixes and
        // verifications from before defect history was recorded).
        var defects = await db.Defects.AsNoTracking()
            .Where(d => d.CreatedById == userId || d.FixedById == userId || d.TestedById == userId)
            .Select(d => new
            {
                d.Id, d.ProjectId, d.Number, d.Summary, d.Status, d.Severity, d.CreatedUtc,
                d.CreatedById, d.FixedById, d.TestedById,
            })
            .ToListAsync(ct);

        var comments = await LoadCommentsAsync(db, userId, ct);
        var uploads = await LoadUploadsAsync(db, userId, ct);

        var memberProjectIds = await db.Projects.AsNoTracking()
            .Where(p => p.Members.Any(m => m.Id == userId))
            .Select(p => p.Id)
            .ToListAsync(ct);

        // --- Shape the rows -------------------------------------------------------------------

        var testRuns = caseEvents
            .Where(e => e.Kind == TestCaseEventKind.ResultChanged)
            .Select(e => new UserTestRun(e.Id, e.ProjectId, e.TestCaseId, e.Number, e.Scenario,
                ParseEnum<TestResult>(e.FromValue), ParseEnum<TestResult>(e.ToValue) ?? TestResult.NotRun, e.OccurredUtc))
            .ToList();

        var raised = defects
            .Where(d => d.CreatedById == userId)
            .Select(d => new UserDefectRaised(d.Id, d.ProjectId, d.Number, d.Summary, d.Status, d.Severity, d.CreatedUtc))
            .ToList();

        var work = new List<UserDefectWork>();
        foreach (var e in defectEvents.Where(e => e.Kind == DefectEventKind.StatusChanged))
        {
            var from = ParseEnum<DefectStatus>(e.FromValue);
            DefectWorkKind? kind = ParseEnum<DefectStatus>(e.ToValue) switch
            {
                DefectStatus.ToCheck => DefectWorkKind.Fixed,
                DefectStatus.Fixed => DefectWorkKind.Verified,
                DefectStatus.NotADefect => DefectWorkKind.NotADefect,
                DefectStatus.NotFixed or DefectStatus.Fixing when from == DefectStatus.ToCheck => DefectWorkKind.SentBack,
                _ => null,
            };
            if (kind is { } k)
            {
                work.Add(new UserDefectWork(e.DefectId, e.ProjectId, e.Number, e.Summary, e.Status, k, e.OccurredUtc));
            }
        }

        // Fixes / verifications older than defect history: the defect's fields still say who.
        foreach (var d in defects)
        {
            if (d.FixedById == userId && !work.Any(w => w.DefectId == d.Id && w.Kind == DefectWorkKind.Fixed))
            {
                work.Add(new UserDefectWork(d.Id, d.ProjectId, d.Number, d.Summary, d.Status, DefectWorkKind.Fixed, null));
            }

            if (d.TestedById == userId && !work.Any(w => w.DefectId == d.Id && w.Kind == DefectWorkKind.Verified))
            {
                work.Add(new UserDefectWork(d.Id, d.ProjectId, d.Number, d.Summary, d.Status, DefectWorkKind.Verified, null));
            }
        }

        // The timeline: every history entry they're the actor of, plus their comments.
        var actorName = ProjectHistory.DisplayName(await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct));
        var (scopeNumbers, tagColours) = await SubjectsAsync(db, projectEvents, ct);
        var timeline = new List<UserTimelineItem>();
        timeline.AddRange(caseEvents.Select(e => new UserTimelineItem(e.ProjectId, new TestCaseActivity(
            new TestCaseEventView(e.Id, e.Kind, userId, actorName, e.OccurredUtc, e.FromValue, e.ToValue),
            e.TestCaseId, e.Scenario, e.Number), null)));
        timeline.AddRange(defectEvents.Select(e => new UserTimelineItem(e.ProjectId, new DefectActivity(
            new DefectEventView(e.Id, e.Kind, userId, actorName, e.OccurredUtc, e.FromValue, e.ToValue),
            e.DefectId, e.Number, e.Summary), null)));
        timeline.AddRange(projectEvents.Select(e => new UserTimelineItem(e.ProjectId, new ProjectActivity(
            new ProjectEventView(e.Id, e.Kind, userId, actorName, e.OccurredUtc, e.FromValue, e.ToValue, e.SubjectId,
                SubjectExists: e.SubjectId is { } s && scopeNumbers.ContainsKey(s),
                TagColour: e.SubjectId is { } t && tagColours.TryGetValue(t, out var colour) ? colour : null,
                ScopeNumber: e.SubjectId is { } n && scopeNumbers.TryGetValue(n, out var number) ? number : null)), null)));
        timeline.AddRange(comments.Select(c => new UserTimelineItem(c.ProjectId, null, c)));

        // --- Per-project sums (always over everything, for the scope picker) ------------------

        var touchedIds = timeline.Select(t => t.ProjectId)
            .Concat(raised.Select(r => r.ProjectId))
            .Concat(work.Select(w => w.ProjectId))
            .Concat(uploads.Select(u => u.ProjectId))
            .Concat(memberProjectIds)
            .ToHashSet();
        var projectRows = await db.Projects.AsNoTracking()
            .Where(p => touchedIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Status })
            .ToListAsync(ct);

        var projects = projectRows
            .Select(p => new UserActivityProject(p.Id, p.Name, p.Status, memberProjectIds.Contains(p.Id),
                Count(p.Id, testRuns, raised, work, comments, uploads, timeline)))
            .OrderByDescending(p => p.Counts.LastActiveUtc ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // --- The chosen scope -----------------------------------------------------------------

        bool InScope(Guid id) => projectId is not { } only || id == only;

        var scopedRuns = testRuns.Where(r => InScope(r.ProjectId)).OrderByDescending(r => r.OccurredUtc).ToList();
        var tally = scopedRuns
            .GroupBy(r => r.To)
            .ToDictionary(g => g.Key, g => g.Count());

        return new UserActivityReport(
            projects,
            projectId,
            Count(projectId, testRuns, raised, work, comments, uploads, timeline),
            tally,
            scopedRuns,
            raised.Where(r => InScope(r.ProjectId)).OrderByDescending(r => r.RaisedUtc).ToList(),
            work.Where(w => InScope(w.ProjectId))
                .OrderByDescending(w => w.OccurredUtc ?? DateTimeOffset.MinValue)
                .ThenByDescending(w => w.Number)
                .ToList(),
            comments.Where(c => InScope(c.ProjectId)).OrderByDescending(c => c.CreatedUtc).ToList(),
            uploads.Where(u => InScope(u.ProjectId)).OrderByDescending(u => u.UploadedUtc).ToList(),
            timeline.Where(t => InScope(t.ProjectId))
                .OrderByDescending(t => t.OccurredUtc)
                .ThenBy(t => t.SameInstantRank)
                .ToList());
    }

    private static UserActivityCounts Count(
        Guid? projectId,
        List<UserTestRun> runs,
        List<UserDefectRaised> raised,
        List<UserDefectWork> work,
        List<UserComment> comments,
        List<UserUpload> uploads,
        List<UserTimelineItem> timeline)
    {
        bool InScope(Guid id) => projectId is not { } only || id == only;

        var recorded = runs.Where(r => InScope(r.ProjectId) && RecordedResults.Contains(r.To)).ToList();
        var scopedWork = work.Where(w => InScope(w.ProjectId)).ToList();
        var scopedUploads = uploads.Where(u => InScope(u.ProjectId)).ToList();

        // Last active: the newest thing they did here (history entries include uploads to items;
        // comment files count via their upload time).
        var times = timeline.Where(t => InScope(t.ProjectId)).Select(t => t.OccurredUtc)
            .Concat(raised.Where(r => InScope(r.ProjectId)).Select(r => r.RaisedUtc))
            .Concat(scopedUploads.Select(u => u.UploadedUtc))
            .Concat(scopedWork.Select(w => w.OccurredUtc).OfType<DateTimeOffset>())
            .ToList();

        return new UserActivityCounts(
            recorded.Count,
            recorded.Select(r => r.TestCaseId).Distinct().Count(),
            raised.Count(r => InScope(r.ProjectId)),
            scopedWork.Where(w => w.Kind == DefectWorkKind.Fixed).Select(w => w.DefectId).Distinct().Count(),
            scopedWork.Where(w => w.Kind == DefectWorkKind.Verified).Select(w => w.DefectId).Distinct().Count(),
            scopedWork.Count(w => w.Kind == DefectWorkKind.SentBack),
            comments.Count(c => InScope(c.ProjectId)),
            scopedUploads.Count,
            scopedUploads.Sum(u => u.SizeBytes),
            // Work known only from a defect's fields still counts as activity, just not when.
            HasActivity: times.Count > 0 || scopedWork.Count > 0,
            LastActiveUtc: times.Count > 0 ? times.Max() : null);
    }

    private static async Task<List<UserComment>> LoadCommentsAsync(ApplicationDbContext db, string userId, CancellationToken ct)
    {
        var onProjects = await db.ProjectComments.AsNoTracking()
            .Where(c => c.AuthorId == userId)
            .Select(c => new { c.Id, c.ProjectId, c.Body, c.CreatedUtc, Title = c.Project!.Name, Files = c.Attachments.Count })
            .ToListAsync(ct);
        var onCases = await db.TestCaseComments.AsNoTracking()
            .Where(c => c.AuthorId == userId)
            .Select(c => new
            {
                c.Id, c.TestCaseId, c.TestCase!.ProjectId, c.TestCase.Number, c.TestCase.Scenario,
                c.Body, c.CreatedUtc, Files = c.Attachments.Count,
            })
            .ToListAsync(ct);
        var onDefects = await db.DefectComments.AsNoTracking()
            .Where(c => c.AuthorId == userId)
            .Select(c => new
            {
                c.Id, c.DefectId, c.Defect!.ProjectId, c.Defect.Number, c.Defect.Summary,
                c.Body, c.CreatedUtc, Files = c.Attachments.Count,
            })
            .ToListAsync(ct);

        // Which page of its thread each comment is on, so links land on it (threads are paged
        // oldest first; see CommentPaging).
        var projectIds = onProjects.Select(c => c.ProjectId).Distinct().ToList();
        var caseIds = onCases.Select(c => c.TestCaseId).Distinct().ToList();
        var defectIds = onDefects.Select(c => c.DefectId).Distinct().ToList();
        var projectThreads = Pages((await db.ProjectComments.AsNoTracking().Where(c => projectIds.Contains(c.ProjectId))
            .Select(c => new { c.Id, Thread = c.ProjectId, c.CreatedUtc }).ToListAsync(ct)).Select(c => (c.Id, c.Thread, c.CreatedUtc)));
        var caseThreads = Pages((await db.TestCaseComments.AsNoTracking().Where(c => caseIds.Contains(c.TestCaseId))
            .Select(c => new { c.Id, Thread = c.TestCaseId, c.CreatedUtc }).ToListAsync(ct)).Select(c => (c.Id, c.Thread, c.CreatedUtc)));
        var defectThreads = Pages((await db.DefectComments.AsNoTracking().Where(c => defectIds.Contains(c.DefectId))
            .Select(c => new { c.Id, Thread = c.DefectId, c.CreatedUtc }).ToListAsync(ct)).Select(c => (c.Id, c.Thread, c.CreatedUtc)));

        return
        [
            .. onProjects.Select(c => new UserComment(c.Id, c.ProjectId, UserActivityTarget.Project, null, 0,
                c.Title, c.Body.Trim(), c.CreatedUtc, projectThreads.GetValueOrDefault(c.Id, 1), c.Files)),
            .. onCases.Select(c => new UserComment(c.Id, c.ProjectId, UserActivityTarget.TestCase, c.TestCaseId, c.Number,
                c.Scenario, c.Body.Trim(), c.CreatedUtc, caseThreads.GetValueOrDefault(c.Id, 1), c.Files)),
            .. onDefects.Select(c => new UserComment(c.Id, c.ProjectId, UserActivityTarget.Defect, c.DefectId, c.Number,
                c.Summary, c.Body.Trim(), c.CreatedUtc, defectThreads.GetValueOrDefault(c.Id, 1), c.Files)),
        ];
    }

    /// <summary>Each comment's 1-based page within its thread.</summary>
    private static Dictionary<Guid, int> Pages(IEnumerable<(Guid Id, Guid Thread, DateTimeOffset CreatedUtc)> rows) =>
        rows.GroupBy(r => r.Thread)
            .SelectMany(thread => thread.OrderBy(r => r.CreatedUtc)
                .Select((r, index) => (r.Id, Page: index / CommentPaging.PageSize + 1)))
            .ToDictionary(r => r.Id, r => r.Page);

    private static async Task<List<UserUpload>> LoadUploadsAsync(ApplicationDbContext db, string userId, CancellationToken ct)
    {
        var rows = await db.Attachments.AsNoTracking()
            .Where(a => a.UploadedById == userId)
            .Select(a => new
            {
                a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedUtc,
                a.ProjectId, ProjectName = a.Project!.Name,
                a.TestCaseId, CaseProjectId = (Guid?)a.TestCase!.ProjectId, CaseNumber = (int?)a.TestCase.Number, CaseScenario = a.TestCase.Scenario,
                a.DefectId, DefectProjectId = (Guid?)a.Defect!.ProjectId, DefectNumber = (int?)a.Defect.Number, DefectSummary = a.Defect.Summary,
                PcProjectId = (Guid?)a.ProjectComment!.ProjectId, PcProjectName = a.ProjectComment.Project!.Name,
                TcCaseId = (Guid?)a.TestCaseComment!.TestCaseId, TcProjectId = (Guid?)a.TestCaseComment.TestCase!.ProjectId,
                TcNumber = (int?)a.TestCaseComment.TestCase.Number, TcScenario = a.TestCaseComment.TestCase.Scenario,
                DcDefectId = (Guid?)a.DefectComment!.DefectId, DcProjectId = (Guid?)a.DefectComment.Defect!.ProjectId,
                DcNumber = (int?)a.DefectComment.Defect.Number, DcSummary = a.DefectComment.Defect.Summary,
            })
            .ToListAsync(ct);

        var uploads = new List<UserUpload>();
        foreach (var a in rows)
        {
            (Guid, UserActivityTarget, Guid?, int, string, bool)? owner =
                a.ProjectId is { } p ? (p, UserActivityTarget.Project, null, 0, a.ProjectName, false)
                : a.TestCaseId is { } t ? (a.CaseProjectId!.Value, UserActivityTarget.TestCase, t, a.CaseNumber ?? 0, a.CaseScenario, false)
                : a.DefectId is { } d ? (a.DefectProjectId!.Value, UserActivityTarget.Defect, d, a.DefectNumber ?? 0, a.DefectSummary, false)
                : a.PcProjectId is { } pc ? (pc, UserActivityTarget.Project, null, 0, a.PcProjectName, true)
                : a.TcCaseId is { } tc ? (a.TcProjectId!.Value, UserActivityTarget.TestCase, tc, a.TcNumber ?? 0, a.TcScenario, true)
                : a.DcDefectId is { } dc ? (a.DcProjectId!.Value, UserActivityTarget.Defect, dc, a.DcNumber ?? 0, a.DcSummary, true)
                : null;
            if (owner is not { } o)
            {
                continue; // an orphan row (shouldn't exist: every owner FK cascades)
            }

            uploads.Add(new UserUpload(a.Id, o.Item1, o.Item2, o.Item3, o.Item4, o.Item5, o.Item6,
                a.FileName, a.ContentType, a.SizeBytes, a.UploadedUtc));
        }

        return uploads;
    }

    /// <summary>For scope and tag history entries (as the project feed shows them): the scopes that
    /// still exist with their IDs, and the tags' current colours.</summary>
    private static async Task<(Dictionary<Guid, int> Scopes, Dictionary<Guid, Tags.TagColour> Tags)> SubjectsAsync(
        ApplicationDbContext db, List<ProjectEvent> events, CancellationToken ct)
    {
        var ids = events.Select(e => e.SubjectId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return ([], []);
        }

        var scopes = await db.TestScopes.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Number, ct);
        var tags = await db.Tags.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Colour, ct);
        return (scopes, tags);
    }

    private static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) ? parsed : null;
}
