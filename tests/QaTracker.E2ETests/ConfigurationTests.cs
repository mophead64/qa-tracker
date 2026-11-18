using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>System settings → Configuration: every setting (secrets masked), live checks and recommendations.</summary>
[TestFixture]
public class ConfigurationTests : E2ETestBase
{
    private ILocator Value(string key) => Page.Locator($"[data-setting='{key}'] [data-setting-value]");

    [Test]
    public async Task Qa_sees_every_setting_masked_runs_checks_and_gets_recommendations()
    {
        await Page.GotoAsync($"{BaseUrl}/admin");
        await Page.Locator("[data-all-configuration]").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Configuration", Exact = true })).ToBeVisibleAsync();

        // Values where they're safe, "Set" for anything secret or an address.
        await Expect(Value("QATRACKER_STORAGE_PROVIDER")).ToHaveTextAsync("S3");
        await Expect(Value("QATRACKER_S3_SECRET_KEY")).ToHaveTextAsync("Set");
        await Expect(Value("QATRACKER_S3_ENDPOINT")).ToHaveTextAsync("Set");
        await Expect(Value("QATRACKER_DB_PASSWORD")).ToHaveTextAsync("Set");
        await Expect(Value("QATRACKER_LIVE_POLL_SECONDS")).ToHaveTextAsync(new Regex(@"^(Default \(10\)|\d+)$"));
        await Expect(Page.Locator("[data-config-settings]")).Not.ToContainTextAsync("qatracker-dev-secret-key");

        // Recommendations for this stack: no SSO, no telemetry, and the first admin's password still configured.
        var advice = Page.Locator("[data-config-advice]");
        await Expect(advice).ToContainTextAsync("Only local accounts can sign in");
        await Expect(advice).ToContainTextAsync("No telemetry");
        await Expect(advice).ToContainTextAsync("The first admin's password is still in the configuration");
        await Expect(advice).Not.ToContainTextAsync("Attachments are switched off");

        // Checks run on demand: the database and Garage really work; SSO isn't set up.
        await Expect(Page.Locator("[data-checks-not-run]")).ToBeVisibleAsync();
        await Page.Locator("[data-run-checks]").ClickAsync();
        await Expect(Page.Locator("[data-check='Database']")).ToHaveAttributeAsync("data-check-outcome", "Ok");
        await Expect(Page.Locator("[data-check='File storage']")).ToHaveAttributeAsync("data-check-outcome", "Ok");
        await Expect(Page.Locator("[data-check='Single sign-on']")).ToHaveAttributeAsync("data-check-outcome", "Skipped");

        // The copyable summary includes the check results and nothing secret.
        var summary = await Page.Locator("[data-config-summary]").GetAttributeAsync("data-copy-link");
        Assert.That(summary, Does.Contain("QATRACKER_STORAGE_PROVIDER: S3"));
        Assert.That(summary, Does.Contain("File storage: Ok"));
        Assert.That(summary, Does.Not.Contain("qatracker-dev-secret-key"));
        Assert.That(summary, Does.Not.Contain(Environment.GetEnvironmentVariable("QATRACKER_E2E_PASSWORD") ?? "E2eP@ssw0rd!"));
    }
}
