using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// End-to-end coverage for Phase 2 project creation. Runs against a live instance
/// (QATRACKER_E2E_BASEURL) and needs a QA account (QATRACKER_E2E_EMAIL /
/// QATRACKER_E2E_PASSWORD); the fixture self-skips when either is missing.
/// </summary>
[TestFixture]
public class ProjectTests : PageTest
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("QATRACKER_E2E_BASEURL")?.TrimEnd('/')
        ?? "http://localhost:5281";

    private static string? Email => Environment.GetEnvironmentVariable("QATRACKER_E2E_EMAIL");
    private static string? Password => Environment.GetEnvironmentVariable("QATRACKER_E2E_PASSWORD");

    [OneTimeSetUp]
    public async Task EnsureReachableAndConfigured()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            Assert.Ignore("QATRACKER_E2E_EMAIL / QATRACKER_E2E_PASSWORD not set.");
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            await client.GetAsync(BaseUrl);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"App not reachable at {BaseUrl}: {ex.Message}");
        }
    }

    [SetUp]
    public async Task SignIn()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.GetByLabel("Email").FillAsync(Email!);
        await Page.GetByLabel("Password").FillAsync(Password!);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        // Landing URL varies (project picker vs. a remembered current project), so just
        // assert we left the login page.
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/Account/Login"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "All projects" }).First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Qa_can_create_a_project_with_a_custom_link_and_switch_to_it()
    {
        var name = $"E2E project {Guid.NewGuid():N}";

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.GetByRole(AriaRole.Link, new() { Name = "New project" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/new$"));

        // The form is an interactive-server component. Input typed before its circuit
        // connects is discarded on the first interactive render, and a submit before then
        // falls back to a plain browser POST. Retry "Add link" until a row sticks — that
        // is the signal the component is live.
        var addLink = Page.GetByRole(AriaRole.Button, new() { Name = "Add link" });
        var labelInput = Page.GetByPlaceholder("Label").First;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            await addLink.ClickAsync();
            try
            {
                await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 1000 });
                break;
            }
            catch (TimeoutException)
            {
                // circuit not connected yet — try again
            }
        }
        await Expect(labelInput).ToBeVisibleAsync();

        await Page.GetByLabel("Name").FillAsync(name);
        await Page.GetByLabel("Notes").FillAsync("Created by the Phase 2 e2e test.");
        await Page.GetByPlaceholder("Label").First.FillAsync("Repo");
        await Page.GetByPlaceholder("https://…").First.FillAsync("https://example.test/repo");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create project" }).ClickAsync();

        // Landed on the new project's dashboard.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Not started")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Repo" })).ToBeVisibleAsync();

        // The switcher summary in the top bar now reflects the current project.
        await Expect(Page.Locator("header summary").Filter(new() { HasTextString = name })).ToBeVisibleAsync();

        // Root now resolves to this project's dashboard.
        var dashboardUrl = Page.Url;
        await Page.GotoAsync($"{BaseUrl}/");
        await Expect(Page).ToHaveURLAsync(dashboardUrl);
    }
}
