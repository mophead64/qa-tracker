using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for Phase 2 project creation.</summary>
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
        await Page.GetByLabel("Notes").FillAsync("Created by the Phase 2 e2e test.");
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
}
