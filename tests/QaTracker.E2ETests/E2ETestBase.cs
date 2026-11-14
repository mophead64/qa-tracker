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

    /// <summary>The display name of the signed-in account, read from the top-bar chip.</summary>
    protected async Task<string> CurrentUserNameAsync() =>
        (await Page.Locator("[data-current-user]").First.InnerTextAsync()).Trim();

    /// <summary>Adds the signed-in account to a project's team via the dashboard modal.</summary>
    protected async Task AddSelfToTeamAsync(string dashboardUrl)
    {
        var me = await CurrentUserNameAsync();
        await Page.GotoAsync(dashboardUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync(),
            Page.GetByRole(AriaRole.Heading, new() { Name = "Add team members" }));
        await Page.Locator("dialog[open]").Locator("label", new() { HasTextString = me })
            .GetByRole(AriaRole.Checkbox).CheckAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#team li").Filter(new() { HasTextString = me })).ToBeVisibleAsync();
    }

    /// <summary>Creates a scope (which moves the project to Active) holding one case per
    /// scenario; returns each case's detail-page URL, in order.</summary>
    protected async Task<List<string>> CreateScopeWithCasesAsync(string dashboardUrl, string scopeName, params string[] scenarios)
    {
        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        await Page.GetByRole(AriaRole.Link, new() { Name = "New scope" }).ClickAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Name").FillAsync(scopeName);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create scope" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = scopeName }));
        var scopeUrl = Page.Url;

        var caseUrls = new List<string>();
        foreach (var scenario in scenarios)
        {
            await Page.GotoAsync(scopeUrl);
            // This scope's own button — the project may already have other scopes.
            await Page.Locator("section[id^='scope-']")
                .Filter(new() { Has = Page.GetByRole(AriaRole.Heading, new() { Name = scopeName }) })
                .GetByRole(AriaRole.Link, new() { Name = "New test case" }).ClickAsync();
            await SubmitUntil(
                async () =>
                {
                    await Page.GetByLabel("Scenario").FillAsync(scenario);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Create test case" }).ClickAsync();
                },
                Page.GetByRole(AriaRole.Heading, new() { Name = scenario }));
            caseUrls.Add(Page.Url);
        }

        return caseUrls;
    }

    /// <summary>Picks an option in a styled <c>FormSelect</c> dropdown (the in-form replacement for a native select).</summary>
    /// <summary>Raises a defect on the project and, optionally, moves it to <paramref name="status"/>.
    /// Returns the defect's URL.</summary>
    protected async Task<string> RaiseDefectAsync(string dashboardUrl, string summary, string? status = null)
    {
        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));

        if (status is not null)
        {
            await RetryUntil(
                async () =>
                {
                    await Page.Locator("summary[aria-label='Change status']").ClickAsync();
                    await Page.GetByRole(AriaRole.Button, new() { Name = status, Exact = true }).ClickAsync();
                },
                Page.Locator("summary[aria-label='Change status']").Filter(new() { HasTextString = status }));
        }

        return Page.Url;
    }

    /// <summary>Opens the (collapsed) Danger zone on an edit page and returns it.</summary>
    protected async Task<ILocator> OpenDangerZoneAsync()
    {
        var zone = Page.Locator("details[data-danger-zone]");
        if (await zone.GetAttributeAsync("open") is null)
        {
            await zone.Locator("summary").ClickAsync();
        }
        await Expect(zone).ToHaveAttributeAsync("open", "");
        return zone;
    }

    protected async Task PickFormSelectAsync(string label, string optionText)
    {
        var toggle = Page.Locator($"details[data-form-select] > summary[aria-label='{label}']");
        await toggle.ClickAsync();
        // Exact, or after a leading ID ("S-2 Checkout" for the scope picker).
        await Page.Locator($"details[data-form-select][open] button[data-select-text='{optionText}'], " +
                           $"details[data-form-select][open] button[data-select-text$=' {optionText}']").First.ClickAsync();
        await Expect(toggle).ToContainTextAsync(optionText);
    }

    /// <summary>Creates a local user (as the signed-in QA fixture account) via the admin UI.</summary>
    protected async Task CreateUserAsync(string email, string fullName, string role, string password = "Str0ng!Passw0rd")
    {
        await Page.GotoAsync($"{BaseUrl}/admin/users/new");
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByLabel("Full name").FillAsync(fullName);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await PickFormSelectAsync("Role", role);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();

        // The one-time sign-in details (a validation failure stays on the form instead), then
        // Done to the list, and confirm the row via search — the list paginates once the shared
        // dev DB has enough accounts.
        await Expect(Page.Locator("[data-credential='email']")).ToHaveTextAsync(email);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Done" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        await Page.GetByPlaceholder("Search users").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = email })).ToBeVisibleAsync();
    }

    /// <summary>Password given to the throwaway users the tests create.</summary>
    protected const string DevPassword = "Str0ng!Passw0rd";

    /// <summary>Adds the named user to a project's team via the dashboard modal.</summary>
    protected async Task AddToTeamAsync(string dashboardUrl, string fullName)
    {
        await Page.GotoAsync(dashboardUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync(),
            Page.GetByRole(AriaRole.Heading, new() { Name = "Add team members" }));
        await Page.Locator("dialog[open]").Locator("label", new() { HasTextString = fullName })
            .GetByRole(AriaRole.Checkbox).CheckAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#team li").Filter(new() { HasTextString = fullName })).ToBeVisibleAsync();
    }

    /// <summary>Signs a fixture-created user in, in its own browser context, and returns that page.</summary>
    protected async Task<IPage> SignInAsync(IBrowserContext context, string email)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/Account/Login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(DevPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(page).Not.ToHaveURLAsync(new Regex("/Account/Login"));
        return page;
    }

    /// <summary>Deletes a user through the admin UI (as the signed-in QA fixture account).</summary>
    protected async Task DeleteUserAsync(string email)
    {
        await Page.GotoAsync($"{BaseUrl}/admin/users?q={email}");
        var row = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = email });
        await row.Locator("a[data-user-edit]").ClickAsync();
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Delete user" }).ClickAsync(),
            Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }));
        await SubmitUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync(),
            Page.GetByRole(AriaRole.Link, new() { Name = "New user" }));
    }
}
