using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for project creation.</summary>
[TestFixture]
public class ProjectTests : E2ETestBase
{
    [Test]
    public async Task Qa_can_create_a_project_with_a_custom_link_and_switch_to_it()
    {
        var name = $"E2E project {Guid.NewGuid():N}";

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.GetByRole(AriaRole.Link, new() { Name = "New project" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/new$"));

        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync(),
            Page.GetByPlaceholder("Label").First);

        await Page.GetByLabel("Name").FillAsync(name);
        await Page.GetByLabel("Notes").FillAsync("Created by the e2e test.");
        await Page.GetByPlaceholder("Label").First.FillAsync("Repo");
        await Page.GetByPlaceholder("https://…").First.FillAsync("https://example.test/repo");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create project" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();
        await Expect(Page.Locator("summary[aria-label='Change project status']").GetByText("Not Started")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Repo" })).ToBeVisibleAsync();

        await Expect(Page.Locator("header summary").Filter(new() { HasTextString = name })).ToBeVisibleAsync();

        var dashboardUrl = Page.Url;

        // The status dropdown submits on pick — move it to Completed.
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change project status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Completed" }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change project status']").Filter(new() { HasTextString = "Completed" }));

        await Page.GotoAsync($"{BaseUrl}/");
        await Expect(Page).ToHaveURLAsync(dashboardUrl);
    }

    [Test]
    public async Task Project_switcher_follows_the_project_list_filter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var activeName = $"Switcher active {suffix}";
        var notStartedName = $"Switcher fresh {suffix}";
        var devEmail = $"e2e-switcher-{suffix}@test.local";

        // One project moved to Active, one left Not Started. A dedicated user, so changing
        // their saved filter can't affect any other test's view of the project list.
        await CreateProjectAsync(activeName);
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change project status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Active", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change project status']").Filter(new() { HasTextString = "Active" }));
        await CreateProjectAsync(notStartedName);
        await CreateUserAsync(devEmail, $"Switcher {suffix}", "Dev", DevPassword);

        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);
        ILocator SwitcherItem(string name) =>
            devPage.Locator("header form[action='projects/switch'] button").Filter(new() { HasTextString = name });

        // Default filter (Active & Upcoming): both are offered.
        await devPage.GotoAsync($"{BaseUrl}/projects");
        await Expect(SwitcherItem(activeName)).ToHaveCountAsync(1);
        await Expect(SwitcherItem(notStartedName)).ToHaveCountAsync(1);

        // Filter to Active only: the switcher drops the Not Started project too.
        await RetryUntil(
            async () =>
            {
                await devPage.Locator("summary[aria-label='Filter projects']").ClickAsync();
                await devPage.GetByRole(AriaRole.Button, new() { Name = "Active", Exact = true }).ClickAsync();
            },
            devPage.Locator("summary[aria-label='Filter projects']").Filter(new() { HasTextString = "Active" }));
        await Expect(SwitcherItem(activeName)).ToHaveCountAsync(1);
        await Expect(SwitcherItem(notStartedName)).ToHaveCountAsync(0);

        await DeleteUserAsync(devEmail);
    }

    [Test]
    public async Task Project_list_can_be_searched()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var keep = $"Alpha search {tag}";
        var hide = $"Beta search {tag}";
        await CreateProjectAsync(keep);
        await CreateProjectAsync(hide);

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.GetByPlaceholder("Search projects").FillAsync($"Alpha search {tag}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"[?&]q=Alpha"));
        await Expect(Page.GetByRole(AriaRole.Button).Filter(new() { HasTextString = keep })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button).Filter(new() { HasTextString = hide })).Not.ToBeVisibleAsync();

        // Clear drops the query and shows the full (paged) list again.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Clear" }).ClickAsync();
        await Expect(Page).Not.ToHaveURLAsync(new Regex(@"[?&]q="));
        await Page.GetByPlaceholder("Search projects").FillAsync($"Beta search {tag}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button).Filter(new() { HasTextString = hide })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Qa_can_assign_a_team_member_from_the_dashboard_and_see_them_grouped_by_role()
    {
        var name = $"E2E team project {Guid.NewGuid():N}";
        var tag = Guid.NewGuid().ToString("N")[..8];
        var memberName = $"Dana Tester {tag}";

        await CreateUserAsync($"e2e-team-qa-{tag}@test.local", memberName, "QA");

        var dashboardUrl = await CreateProjectAsync(name);
        await Page.GotoAsync(dashboardUrl);

        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync(),
            Page.GetByRole(AriaRole.Heading, new() { Name = "Add team members" }));
        await Page.Locator("dialog[open]").Locator("label", new() { HasTextString = memberName })
            .GetByRole(AriaRole.Checkbox).CheckAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/[0-9a-fA-F-]{36}$"));
        var memberRow = Page.Locator("li").Filter(new() { HasTextString = memberName });
        await Expect(memberRow).ToBeVisibleAsync();

        // Removing them empties the team again.
        await memberRow.GetByRole(AriaRole.Button, new() { Name = $"Remove {memberName}" }).ClickAsync();
        await Expect(Page.GetByText("No team members assigned yet.")).ToBeVisibleAsync();
    }
}
