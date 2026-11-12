namespace QaTracker.Web.Projects;

/// <summary>Which project statuses the All Projects page lists. Stored per user so the
/// choice persists between logins.</summary>
public enum ProjectListFilter
{
    /// <summary>Active, Upcoming and Not Started projects — hides Completed and Cancelled. The default.</summary>
    ActiveAndUpcoming = 0,
    All = 1,
    Active = 2,
}
