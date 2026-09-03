namespace QaTracker.Web.Projects;

/// <summary>
/// Lifecycle of a project.
/// <list type="bullet">
///   <item><see cref="NotStarted"/> — created, but nothing has been added inside it yet.</item>
///   <item><see cref="InFlight"/> — work has begun; set automatically once the first item
///     (test case, defect, …) is created in the project.</item>
///   <item><see cref="Complete"/> — a QA or Dev has explicitly marked the project done.</item>
/// </list>
/// </summary>
public enum ProjectStatus
{
    NotStarted = 0,
    InFlight = 1,
    Complete = 2,
}
