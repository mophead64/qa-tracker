using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// End-to-end coverage for Phase 12 (SSO / OIDC). Self-skips unless QATRACKER_E2E_SSO=1,
/// since it needs the docker-compose Keycloak up and the app configured with
/// QATRACKER_AUTH_PROVIDER=Keycloak. Signs in the seeded realm user (qa@example.com /
/// Passw0rd!) through the real Keycloak login form and checks the app provisioned them as
/// a provider-managed QA.
/// </summary>
[TestFixture]
public class SsoTests : PageTest
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("QATRACKER_E2E_BASEURL")?.TrimEnd('/')
        ?? "http://localhost:5281";

    private const string SsoEmail = "qa@example.com";
    private const string SsoPassword = "Passw0rd!";

    [OneTimeSetUp]
    public async Task EnsureEnabledAndReachable()
    {
        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SSO") != "1")
        {
            Assert.Ignore("QATRACKER_E2E_SSO != 1 — SSO end-to-end test needs the compose Keycloak stack.");
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

    [Test]
    public async Task User_signs_in_through_keycloak_and_is_provisioned_as_a_managed_qa()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");

        var ssoButton = Page.Locator("button", new() { HasTextString = "Sign in with" });
        await Expect(ssoButton).ToBeVisibleAsync();
        await ssoButton.ClickAsync();

        // Keycloak's own login page.
        await Page.WaitForURLAsync(new Regex("/realms/qatracker/"));
        await Page.Locator("#username").FillAsync(SsoEmail);
        await Page.Locator("#password").FillAsync(SsoPassword);
        await Page.Locator("#kc-login").ClickAsync();

        // Back in the app, signed in.
        await Page.WaitForURLAsync(new Regex($"^{Regex.Escape(BaseUrl)}/"));
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/Account/Login"));

        // Settings shows the account is provider-managed and QA, with no credential forms.
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.GetByText("(SSO)")).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex("managed by", RegexOptions.IgnoreCase))).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Change password" })).ToHaveCountAsync(0);
        await Expect(Page.Locator(".badge").Filter(new() { HasTextString = "QA" })).ToBeVisibleAsync();

        // Admin user list flags them as SSO.
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        var row = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = SsoEmail });
        await Expect(row.GetByText("SSO")).ToBeVisibleAsync();

        // Logout is RP-initiated (ends the Keycloak session too) and lands back on the app.
        await Page.Locator("header details summary").Last.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).ClickAsync();
        await Page.WaitForURLAsync(new Regex("/Account/Login"));
        await Expect(Page.Locator("button", new() { HasTextString = "Sign in with" })).ToBeVisibleAsync();
    }
}
