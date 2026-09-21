namespace QaTracker.Web.Projects;

/// <summary>Which project statuses the All Projects page lists. Stored per user so the
/// choice persists between logins.</summary>
public enum ProjectListFilter
{
    /// <summary>Active and Inactive (not yet started) projects — hides Completed. The default.</summary>
    ActiveAndUpcoming = 0,
    All = 1,
    Active = 2,
}
