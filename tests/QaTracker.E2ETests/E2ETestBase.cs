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
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/Account/Login"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "All projects" }).First).ToBeVisibleAsync();
    }

    /// <summary>
    /// Interactive-server pages don't wire up event handlers until their circuit
    /// connects, and input typed before then is dropped on the first render. Retry an
    /// action until <paramref name="probe"/> shows the page is live.
    /// </summary>
    protected async Task RetryUntil(Func<Task> action, ILocator probe, int attempts = 15)
    {
        for (var i = 0; i < attempts; i++)
        {
            await action();
            try
            {
                await probe.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 1000 });
                return;
            }
            catch (TimeoutException)
            {
                // not connected yet
            }
        }

        await Expect(probe).ToBeVisibleAsync();
    }

    /// <summary>
    /// Fill + submit an interactive form, retrying until <paramref name="success"/>
    /// appears. Handles the case where an early submit reloaded the page (circuit not
    /// yet connected) and cleared the fields.
    /// </summary>
    protected async Task SubmitUntil(Func<Task> fillAndSubmit, ILocator success, int attempts = 12)
    {
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                await fillAndSubmit();
            }
            catch (PlaywrightException)
            {
                // fields already gone — a previous attempt navigated
            }

            try
            {
                await success.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 1500 });
                return;
            }
            catch (TimeoutException)
            {
                // not submitted yet
            }
        }

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
