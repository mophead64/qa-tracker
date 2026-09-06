namespace QaTracker.Web.Projects;

/// <summary>
/// Lifecycle of a project. Display labels (see <see cref="ProjectStatusDisplay"/>) are
/// "Inactive" / "Active" / "Completed"; the enum names are kept for storage stability.
/// <list type="bullet">
///   <item><see cref="NotStarted"/> ("Inactive") — created, but nothing has been added inside it yet.</item>
///   <item><see cref="InFlight"/> ("Active") — work has begun; set automatically once the first item
///     (test case, defect, …) is created in the project.</item>
///   <item><see cref="Complete"/> ("Completed") — a QA or Dev has explicitly marked the project done.</item>
/// </list>
/// </summary>
public enum ProjectStatus
{
    NotStarted = 0,
    InFlight = 1,
    Complete = 2,
}
