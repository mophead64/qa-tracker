using QaTracker.Web.Components.Layout;

namespace QaTracker.UnitTests.Components;

/// <summary>Which side-nav entry each page lights up — every page under a project keeps its area lit.</summary>
public sealed class NavSectionTests
{
    private static readonly Guid Current = Guid.Parse("3f2b8c1e-0000-4000-8000-000000000001");
    private const string P = "projects/3f2b8c1e-0000-4000-8000-000000000001";
    private const string Id = "3f2b8c1e-0000-4000-8000-0000000000aa";

    [Theory]
    // Test cases: the plan, a case, its edit form, import, and every scope page and form.
    [InlineData(P + "/test-cases", NavArea.TestCases)]
    [InlineData(P + "/test-cases/" + Id, NavArea.TestCases)]
    [InlineData(P + "/test-cases/" + Id + "/edit", NavArea.TestCases)]
    [InlineData(P + "/test-cases/import", NavArea.TestCases)]
    [InlineData(P + "/scopes/" + Id, NavArea.TestCases)]
    [InlineData(P + "/scopes/new", NavArea.TestCases)]
    [InlineData(P + "/scopes/" + Id + "/edit", NavArea.TestCases)]
    [InlineData(P + "/scopes/" + Id + "/test-cases/new?fromDefect=x", NavArea.TestCases)]
    // Defects: the list, a defect, and its forms.
    [InlineData(P + "/defects", NavArea.Defects)]
    [InlineData(P + "/defects/new", NavArea.Defects)]
    [InlineData(P + "/defects/" + Id, NavArea.Defects)]
    [InlineData(P + "/defects/" + Id + "/edit", NavArea.Defects)]
    // Board: the board, new items, an item and its edit form.
    [InlineData(P + "/board", NavArea.Board)]
    [InlineData(P + "/board/new?type=Story", NavArea.Board)]
    [InlineData(P + "/board/" + Id, NavArea.Board)]
    [InlineData(P + "/board/" + Id + "/edit", NavArea.Board)]
    // Dashboard: the project itself, its edit form and its activity.
    [InlineData(P, NavArea.Dashboard)]
    [InlineData(P + "?commentsPage=2#comments", NavArea.Dashboard)]
    [InlineData(P + "/edit", NavArea.Dashboard)]
    [InlineData(P + "/activity", NavArea.Dashboard)]
    // All projects, and System settings with everything under it.
    [InlineData("projects", NavArea.AllProjects)]
    [InlineData("projects/new", NavArea.AllProjects)]
    [InlineData("projects?q=checkout", NavArea.AllProjects)]
    [InlineData("admin", NavArea.SystemSettings)]
    [InlineData("admin/users/abc/edit", NavArea.SystemSettings)]
    [InlineData("admin/tags", NavArea.SystemSettings)]
    // Pages with no entry of their own.
    [InlineData("", NavArea.None)]
    [InlineData("settings", NavArea.None)]
    [InlineData("Account/Login", NavArea.None)]
    public void Every_page_lights_its_area(string path, NavArea expected) =>
        Assert.Equal(expected, NavSection.For(path, Current));

    [Theory]
    [InlineData("projects/3f2b8c1e-0000-4000-8000-000000000002/test-cases")]
    [InlineData("projects/3f2b8c1e-0000-4000-8000-000000000002")]
    public void Another_projects_pages_show_as_browsing_all_projects(string path) =>
        Assert.Equal(NavArea.AllProjects, NavSection.For(path, Current));

    [Fact]
    public void Without_a_current_project_a_projects_pages_show_as_all_projects() =>
        Assert.Equal(NavArea.AllProjects, NavSection.For(P + "/defects", null));
}
