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
        await Expect(Page.GetByText("Not started")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Repo" })).ToBeVisibleAsync();

        await Expect(Page.Locator("header summary").Filter(new() { HasTextString = name })).ToBeVisibleAsync();

        var dashboardUrl = Page.Url;
        await Page.GotoAsync($"{BaseUrl}/");
        await Expect(Page).ToHaveURLAsync(dashboardUrl);
    }

    [Test]
    public async Task Qa_can_assign_a_team_member_and_see_it_on_the_dashboard_and_project_list()
    {
        var name = $"E2E team project {Guid.NewGuid():N}";
        var me = Environment.GetEnvironmentVariable("QATRACKER_E2E_DISPLAYNAME") ?? "E2E QA Bot";

        var dashboardUrl = await CreateProjectAsync(name);

        await Page.GotoAsync($"{dashboardUrl}/edit");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Add a team member").SelectOptionAsync(new SelectOptionValue { Label = $"[QA] {me}" });
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add member" }).ClickAsync();
            },
            Page.GetByText(me).First);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Team", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText(me).First).ToBeVisibleAsync();

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Your projects" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "All projects" })).ToBeVisibleAsync();
    }
}
