namespace QaTracker.Web.Projects;

/// <summary>Presentation helpers for <see cref="ProjectStatus"/>.</summary>
public static class ProjectStatusDisplay
{
    public static string Label(ProjectStatus status) => status switch
    {
        ProjectStatus.NotStarted => "Not started",
        ProjectStatus.InFlight => "In flight",
        ProjectStatus.Complete => "Complete",
        _ => status.ToString(),
    };

    public static string BadgeClass(ProjectStatus status) => status switch
    {
        ProjectStatus.NotStarted => "badge badge-gray",
        ProjectStatus.InFlight => "badge badge-brand",
        ProjectStatus.Complete => "badge badge-blue",
        _ => "badge badge-gray",
    };

    /// <summary>Solid, fully colour-filled chip for the editable status control on the dashboard.</summary>
    public static string ChipClass(ProjectStatus status) => status switch
    {
        ProjectStatus.NotStarted => "chip chip-gray",
        ProjectStatus.InFlight => "chip chip-brand",
        ProjectStatus.Complete => "chip chip-blue",
        _ => "chip chip-gray",
    };
}
