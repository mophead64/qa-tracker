using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Projects;

/// <summary>A history row that can sit on the project activity feed.</summary>
public interface ITimelineEvent
{
    DateTimeOffset OccurredUtc { get; }
}

/// <summary>Which history the activity feed shows (the <c>?show=</c> filter on the activity page).</summary>
public enum ActivityFilter
{
    All,
    Project,
    TestCases,
    Defects,
}

/// <summary>A project history entry with the actor's name resolved.</summary>
/// <param name="SubjectExists">For scope entries: whether the scope still exists (so it can be linked).</param>
public sealed record ProjectEventView(
    Guid Id,
    ProjectEventKind Kind,
    string? ActorId,
    string ActorName,
    DateTimeOffset OccurredUtc,
    string? FromValue,
    string? ToValue,
    Guid? SubjectId,
    bool SubjectExists)
{
    public ProjectStatus? FromStatus => Enum.TryParse<ProjectStatus>(FromValue, out var s) ? s : null;
    public ProjectStatus? ToStatus => Enum.TryParse<ProjectStatus>(ToValue, out var s) ? s : null;
}

/// <summary>One row of a project's activity feed: an entry from the project's own history, or
/// from one of its test cases' or defects'.</summary>
public abstract record ProjectActivityItem(Guid Id, DateTimeOffset OccurredUtc)
{
    /// <summary>Order among entries recorded in the same instant (lower reads first).</summary>
    public abstract int SameInstantRank { get; }
}

public sealed record ProjectActivity(ProjectEventView Event) : ProjectActivityItem(Event.Id, Event.OccurredUtc)
{
    public override int SameInstantRank => Event.Kind == ProjectEventKind.Created ? 9 : 5;
}

public sealed record TestCaseActivity(TestCaseEventView Event, Guid TestCaseId, string Scenario)
    : ProjectActivityItem(Event.Id, Event.OccurredUtc)
{
    public override int SameInstantRank => Event.Kind == TestCaseEventKind.Created ? 9 : 5;
}

public sealed record DefectActivity(DefectEventView Event, Guid DefectId, int Number, string Summary)
    : ProjectActivityItem(Event.Id, Event.OccurredUtc)
{
    public override int SameInstantRank => DefectHistory.SameInstantRank(Event.Kind);
}

/// <summary>One page of the feed, newest first. <paramref name="NextBefore"/> is the cursor for
/// the next (older) page, or null when there's nothing older.</summary>
public sealed record ProjectActivityPage(IReadOnlyList<ProjectActivityItem> Items, DateTimeOffset? NextBefore);

/// <summary>
/// A project's activity feed: its own history (<see cref="ProjectEvent"/>) merged with the
/// headline moments of its test cases' and defects' histories — creation, result / status
/// changes and assignments. Edits, file and link churn stay on each item's own History modal.
///
/// <para>Paged by a time cursor ("everything before T"), so each page is a handful of
/// index-ordered queries however long the history gets; a page never splits entries recorded in
/// the same instant (a defect's status + assignee change), so nothing is skipped between pages.</para>
/// </summary>
public sealed class ProjectActivityService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public const int DashboardSize = 5;
    public const int PageSize = 25;

    /// <summary>Test case history kinds the project feed shows.</summary>
    public static readonly IReadOnlyList<TestCaseEventKind> TestCaseKinds =
        [TestCaseEventKind.Created, TestCaseEventKind.ResultChanged];

    /// <summary>Defect history kinds the project feed shows.</summary>
    public static readonly IReadOnlyList<DefectEventKind> DefectKinds =
        [DefectEventKind.Created, DefectEventKind.StatusChanged, DefectEventKind.AssigneeChanged];

    public async Task<ProjectActivityPage> ListAsync(
        Guid projectId,
        ActivityFilter filter = ActivityFilter.All,
        DateTimeOffset? before = null,
        int size = PageSize,
        CancellationToken ct = default)
    {
        size = Math.Max(1, size);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sources = Sources(db, projectId, filter);

        // Each source's newest size+1 entries: enough to fill the page and see past its end.
        var merged = await FetchAsync(db, sources, new Window(before, null, size + 1), ct);
        if (merged.Count <= size)
        {
            return new ProjectActivityPage(await WithScopeLinksAsync(db, merged, ct), null);
        }

        var boundary = merged[size - 1].OccurredUtc;
        if (merged[size].OccurredUtc < boundary)
        {
            return new ProjectActivityPage(await WithScopeLinksAsync(db, merged.Take(size).ToList(), ct), boundary);
        }

        // Entries share the boundary instant across the page edge — take that whole instant so the
        // next page (strictly before it) doesn't skip any.
        var page = await FetchAsync(db, sources, new Window(before, boundary, null), ct);
        var hasOlder = merged.Any(i => i.OccurredUtc < boundary)
            || (await FetchAsync(db, sources, new Window(boundary, null, 1), ct)).Count > 0;
        return new ProjectActivityPage(await WithScopeLinksAsync(db, page, ct), hasOlder ? boundary : null);
    }

    /// <summary>Parses the <c>?show=</c> value ("project", "test-cases", "defects"); anything else is All.</summary>
    public static ActivityFilter ParseFilter(string? value) => value?.ToLowerInvariant() switch
    {
        "project" => ActivityFilter.Project,
        "test-cases" => ActivityFilter.TestCases,
        "defects" => ActivityFilter.Defects,
        _ => ActivityFilter.All,
    };

    public static string? FilterValue(ActivityFilter filter) => filter switch
    {
        ActivityFilter.Project => "project",
        ActivityFilter.TestCases => "test-cases",
        ActivityFilter.Defects => "defects",
        _ => null,
    };

    /// <summary>The cursor as it travels in a URL (UTC ticks).</summary>
    public static string FormatCursor(DateTimeOffset when) =>
        when.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static DateTimeOffset? ParseCursor(string? value) =>
        long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticks)
        && ticks > 0 && ticks <= DateTimeOffset.MaxValue.UtcTicks
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;

    /// <summary>A time slice of the history: before <see cref="Before"/> (exclusive), at or after
    /// <see cref="NotBefore"/>, newest first, at most <see cref="Take"/> per source.</summary>
    private sealed record Window(DateTimeOffset? Before, DateTimeOffset? NotBefore, int? Take);

    private delegate Task<List<ProjectActivityItem>> Source(Window window, CancellationToken ct);

    private static List<Source> Sources(ApplicationDbContext db, Guid projectId, ActivityFilter filter)
    {
        var sources = new List<Source>();
        if (filter is ActivityFilter.All or ActivityFilter.Project)
        {
            sources.Add(async (w, ct) =>
            {
                var rows = await QueryAsync(db,
                    db.ProjectEvents.AsNoTracking().Include(e => e.Actor).Where(e => e.ProjectId == projectId), w, ct);
                return rows.Select(e => (ProjectActivityItem)new ProjectActivity(new ProjectEventView(
                    e.Id, e.Kind, e.ActorId, ProjectHistory.DisplayName(e.Actor), e.OccurredUtc,
                    e.FromValue, e.ToValue, e.SubjectId, SubjectExists: false))).ToList();
            });
        }

        if (filter is ActivityFilter.All or ActivityFilter.TestCases)
        {
            sources.Add(async (w, ct) =>
            {
                var rows = await QueryAsync(db,
                    db.TestCaseEvents.AsNoTracking().Include(e => e.Actor).Include(e => e.TestCase)
                        .Where(e => e.TestCase!.TestScope!.ProjectId == projectId && TestCaseKinds.Contains(e.Kind)), w, ct);
                return rows.Select(e => (ProjectActivityItem)new TestCaseActivity(
                    new TestCaseEventView(e.Id, e.Kind, e.ActorId, ProjectHistory.DisplayName(e.Actor),
                        e.OccurredUtc, e.FromValue, e.ToValue),
                    e.TestCaseId, e.TestCase!.Scenario)).ToList();
            });
        }

        if (filter is ActivityFilter.All or ActivityFilter.Defects)
        {
            sources.Add(async (w, ct) =>
            {
                var rows = await QueryAsync(db,
                    db.DefectEvents.AsNoTracking().Include(e => e.Actor).Include(e => e.Defect)
                        .Where(e => e.Defect!.ProjectId == projectId && DefectKinds.Contains(e.Kind)), w, ct);
                return rows.Select(e => (ProjectActivityItem)new DefectActivity(
                    new DefectEventView(e.Id, e.Kind, e.ActorId, ProjectHistory.DisplayName(e.Actor),
                        e.OccurredUtc, e.FromValue, e.ToValue),
                    e.DefectId, e.Defect!.Number, e.Defect.Summary)).ToList();
            });
        }

        return sources;
    }

    private static async Task<List<ProjectActivityItem>> FetchAsync(
        ApplicationDbContext db, List<Source> sources, Window window, CancellationToken ct)
    {
        // One context: run the sources one after another.
        var all = new List<ProjectActivityItem>();
        foreach (var source in sources)
        {
            all.AddRange(await source(window, ct));
        }

        return all
            .OrderByDescending(i => i.OccurredUtc)
            .ThenBy(i => i.SameInstantRank)
            .ToList();
    }

    /// <summary>
    /// Applies the window. On PostgreSQL that's in SQL (ordered by the OccurredUtc index, LIMITed).
    /// SQLite — the unit tests' database — can't compare or order DateTimeOffset columns, so there
    /// the source's rows are loaded and windowed in memory.
    /// </summary>
    private static async Task<List<T>> QueryAsync<T>(ApplicationDbContext db, IQueryable<T> query, Window w, CancellationToken ct)
        where T : class, ITimelineEvent
    {
        if (db.Database.IsNpgsql())
        {
            if (w.Before is { } before) query = query.Where(e => e.OccurredUtc < before);
            if (w.NotBefore is { } notBefore) query = query.Where(e => e.OccurredUtc >= notBefore);
            query = query.OrderByDescending(e => e.OccurredUtc);
            if (w.Take is { } take) query = query.Take(take);
            return await query.ToListAsync(ct);
        }

        IEnumerable<T> rows = await query.ToListAsync(ct);
        if (w.Before is { } b) rows = rows.Where(e => e.OccurredUtc < b);
        if (w.NotBefore is { } nb) rows = rows.Where(e => e.OccurredUtc >= nb);
        rows = rows.OrderByDescending(e => e.OccurredUtc);
        if (w.Take is { } t) rows = rows.Take(t);
        return rows.ToList();
    }

    /// <summary>Marks the scope entries whose scope still exists, so the feed can link them.</summary>
    private static async Task<IReadOnlyList<ProjectActivityItem>> WithScopeLinksAsync(
        ApplicationDbContext db, List<ProjectActivityItem> items, CancellationToken ct)
    {
        var scopeIds = items.OfType<ProjectActivity>()
            .Select(p => p.Event.SubjectId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        if (scopeIds.Count == 0)
        {
            return items;
        }

        var existing = (await db.TestScopes.AsNoTracking()
            .Where(s => scopeIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct)).ToHashSet();

        return items
            .Select(i => i is ProjectActivity p && p.Event.SubjectId is { } id && existing.Contains(id)
                ? p with { Event = p.Event with { SubjectExists = true } }
                : i)
            .ToList();
    }
}
