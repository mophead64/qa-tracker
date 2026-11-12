namespace QaTracker.Web.Projects;

/// <summary>Presentation helpers for <see cref="ProjectStatus"/>.</summary>
public static class ProjectStatusDisplay
{
    /// <summary>Every status in lifecycle order — the order the status menu lists them.</summary>
    public static readonly IReadOnlyList<ProjectStatus> InOrder =
    [
        ProjectStatus.NotStarted,
        ProjectStatus.Upcoming,
        ProjectStatus.InFlight,
        ProjectStatus.Complete,
        ProjectStatus.Cancelled,
    ];

    public static string Label(ProjectStatus status) => status switch
    {
        ProjectStatus.NotStarted => "Not Started",
        ProjectStatus.Upcoming => "Upcoming",
        ProjectStatus.InFlight => "Active",
        ProjectStatus.Complete => "Completed",
        ProjectStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string BadgeClass(ProjectStatus status) => status switch
    {
        ProjectStatus.Upcoming => "badge badge-amber",
        ProjectStatus.InFlight => "badge badge-blue",
        ProjectStatus.Complete => "badge badge-brand",
        ProjectStatus.Cancelled => "badge badge-red",
        _ => "badge badge-gray",
    };

    /// <summary>Solid, fully colour-filled chip for the editable status control on the dashboard.</summary>
    public static string ChipClass(ProjectStatus status) => status switch
    {
        ProjectStatus.Upcoming => "chip chip-amber",
        ProjectStatus.InFlight => "chip chip-blue",
        ProjectStatus.Complete => "chip chip-brand",
        ProjectStatus.Cancelled => "chip chip-red",
        _ => "chip chip-gray",
    };

    /// <summary>Statuses still to do or in progress — what the "Active &amp; Upcoming" filter keeps.</summary>
    public static bool IsOpen(ProjectStatus status) =>
        status is ProjectStatus.NotStarted or ProjectStatus.Upcoming or ProjectStatus.InFlight;
}
