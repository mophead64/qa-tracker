namespace QaTracker.Web.Components.Layout;

/// <summary>The side-nav entries a page can light up.</summary>
public enum NavArea
{
    None,
    AllProjects,
    Dashboard,
    TestCases,
    Defects,
    Board,
    SystemSettings,
}

/// <summary>
/// Which side-nav entry a page belongs to. NavLink's prefix matching couldn't express it: scope
/// pages live at /projects/{p}/scopes/… (not under …/test-cases), and the project's edit and
/// activity pages aren't under the dashboard's exact URL. So every page under a project maps to
/// its area here — test cases, scopes, import and their forms are all Test cases; defects and
/// their forms are Defects; the board and its items are Board; everything else in the project (edit, activity) is Dashboard.
/// </summary>
public static class NavSection
{
    /// <param name="path">The base-relative path, e.g. "projects/{id}/scopes/{s}/edit" (any query or fragment is ignored).</param>
    /// <param name="currentProjectId">The user's current project — the one the nav's project links point at.</param>
    public static NavArea For(string path, Guid? currentProjectId)
    {
        var parts = path.Split('?', '#')[0].Trim('/').ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return NavArea.None;
        }

        if (parts[0] == "admin")
        {
            return NavArea.SystemSettings;
        }

        if (parts[0] != "projects")
        {
            return NavArea.None;
        }

        // /projects and /projects/new (and anything else that isn't a project id).
        if (parts.Length == 1 || !Guid.TryParse(parts[1], out var projectId))
        {
            return NavArea.AllProjects;
        }

        // Another project's pages: the nav's project links are for the current project, so lighting
        // one of them would point somewhere else — show it as browsing All projects instead.
        if (projectId != currentProjectId)
        {
            return NavArea.AllProjects;
        }

        return parts.Length > 2 ? parts[2] switch
        {
            "test-cases" or "scopes" => NavArea.TestCases,
            "defects" => NavArea.Defects,
            "board" => NavArea.Board,
            _ => NavArea.Dashboard,
        } : NavArea.Dashboard;
    }
}
