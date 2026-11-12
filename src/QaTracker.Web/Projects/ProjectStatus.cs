namespace QaTracker.Web.Projects;

/// <summary>
/// Lifecycle of a project. Display labels (see <see cref="ProjectStatusDisplay"/>) are
/// "Not Started" / "Upcoming" / "Active" / "Completed" / "Cancelled"; the enum names are kept for
/// storage stability (stored as strings, so values can be added without a schema change).
/// <list type="bullet">
///   <item><see cref="NotStarted"/> ("Not Started") — created, with no testing dates and nothing added inside it yet.</item>
///   <item><see cref="Upcoming"/> ("Upcoming") — testing dates are set but work hasn't begun; set automatically
///     when a Not Started project gets a testing window (and back to Not Started if the window is removed).</item>
///   <item><see cref="InFlight"/> ("Active") — work has begun; set automatically once the first item
///     (test case, defect, …) is created in a Not Started or Upcoming project.</item>
///   <item><see cref="Complete"/> ("Completed") — a QA or Dev has explicitly marked the project done.</item>
///   <item><see cref="Cancelled"/> ("Cancelled") — set by hand when the project won't go ahead / be finished.</item>
/// </list>
/// </summary>
public enum ProjectStatus
{
    NotStarted = 0,
    InFlight = 1,
    Complete = 2,
    Upcoming = 3,
    Cancelled = 4,
}
