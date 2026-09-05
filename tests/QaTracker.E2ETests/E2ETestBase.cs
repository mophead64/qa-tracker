using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// Shared setup for authenticated end-to-end tests. Runs against a live instance
/// (QATRACKER_E2E_BASEURL) and needs a QA account (QATRACKER_E2E_EMAIL /
/// QATRACKER_E2E_PASSWORD); fixtures self-skip when either is missing or the app is down.
/// </summary>
public abstract class E2ETestBase : PageTest
{
    protected static string BaseUrl =>
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
        // Exact: the login page also offers a "Sign in with <provider>" button when SSO is on.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/Account/Login"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "All projects" }).First).ToBeVisibleAsync();
    }

    /// <summary>
    /// Run <paramref name="action"/> once, then wait for <paramref name="probe"/>. The UI is
    /// static server-side rendering — forms post and the page re-renders synchronously — so no
    /// retry loop is needed; the <paramref name="attempts"/> parameter is kept only so existing
    /// call sites compile unchanged.
    /// </summary>
    protected async Task RetryUntil(Func<Task> action, ILocator probe, int attempts = 15)
    {
        await action();
        await Expect(probe).ToBeVisibleAsync();
    }

    /// <summary>Fill + submit a form, then wait for <paramref name="success"/>.</summary>
    protected async Task SubmitUntil(Func<Task> fillAndSubmit, ILocator success, int attempts = 12)
    {
        await fillAndSubmit();
        await Expect(success).ToBeVisibleAsync();
    }

    /// <summary>Creates a project through the UI and returns its dashboard URL.</summary>
    protected async Task<string> CreateProjectAsync(string name)
    {
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.GetByRole(AriaRole.Link, new() { Name = "New project" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects/new$"));

        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync(),
            Page.GetByPlaceholder("Label").First);

        await Page.GetByLabel("Name").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create project" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}$"));
        return Page.Url;
    }
}
