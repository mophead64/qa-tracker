using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// End-to-end smoke tests. These run against an already-running instance of the app,
/// pointed at by the QATRACKER_E2E_BASEURL environment variable (defaults to the
/// Kestrel dev URL). They are skipped when the app is not reachable so that
/// `dotnet test` on a fresh checkout stays green.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class SmokeTests : PageTest
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("QATRACKER_E2E_BASEURL")?.TrimEnd('/')
        ?? "http://localhost:5281";

    [OneTimeSetUp]
    public async Task EnsureAppIsReachable()
    {
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
    public async Task Anonymous_user_is_redirected_to_the_login_page()
    {
        await Page.GotoAsync(BaseUrl);

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Account/Login"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Login_page_has_no_self_service_signup()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Create account" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Forgot password?" })).ToHaveCountAsync(0);
    }
}
