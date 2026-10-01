namespace QaTracker.Web.Projects;

/// <summary>Presentation helpers for <see cref="ProjectStatus"/>.</summary>
public static class ProjectStatusDisplay
{
    public static string Label(ProjectStatus status) => status switch
    {
        ProjectStatus.NotStarted => "Not Started",
        ProjectStatus.InFlight => "Active",
        ProjectStatus.Complete => "Completed",
        _ => status.ToString(),
    };

    public static string BadgeClass(ProjectStatus status) => status switch
    {
        ProjectStatus.NotStarted => "badge badge-gray",
        ProjectStatus.InFlight => "badge badge-blue",
        ProjectStatus.Complete => "badge badge-brand",
        _ => "badge badge-gray",
    };

    /// <summary>Solid, fully colour-filled chip for the editable status control on the dashboard.</summary>
    public static string ChipClass(ProjectStatus status) => status switch
    {
        ProjectStatus.NotStarted => "chip chip-gray",
        ProjectStatus.InFlight => "chip chip-blue",
        ProjectStatus.Complete => "chip chip-brand",
        _ => "chip chip-gray",
    };
}
