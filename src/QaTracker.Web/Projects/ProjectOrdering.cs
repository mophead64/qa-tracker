namespace QaTracker.Web.Projects;

/// <summary>
/// How lists of projects are ordered for picking one — the All Projects page and the top-bar
/// switcher agree: Active, then Upcoming, Not Started, Completed, Cancelled. Active and Upcoming
/// projects are soonest testing start first (undated last), so the work that's on now or next
/// leads; the rest are by name.
/// </summary>
public static class ProjectOrdering
{
    /// <summary>The statuses in list order — the switcher's group order.</summary>
    public static readonly IReadOnlyList<ProjectStatus> StatusOrder =
    [
        ProjectStatus.InFlight,
        ProjectStatus.Upcoming,
        ProjectStatus.NotStarted,
        ProjectStatus.Complete,
        ProjectStatus.Cancelled,
    ];

    /// <summary>Whether projects with this status are ordered by testing start date.</summary>
    public static bool ByStartDate(ProjectStatus status) => status is ProjectStatus.InFlight or ProjectStatus.Upcoming;

    /// <summary>Projects in list order: by status group, then start date (Active / Upcoming) and name.</summary>
    public static List<Project> Order(IEnumerable<Project> projects) => projects
        .OrderBy(p => Rank(p.Status))
        .ThenBy(p => ByStartDate(p.Status) ? p.TestingStartDate ?? DateOnly.MaxValue : DateOnly.MinValue)
        .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static int Rank(ProjectStatus status)
    {
        var i = ((IList<ProjectStatus>)StatusOrder).IndexOf(status);
        return i < 0 ? StatusOrder.Count : i;
    }
}
