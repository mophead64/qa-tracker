using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The per-user result filter on the test cases page.</summary>
[TestFixture]
public class TestCaseFilterTests : E2ETestBase
{
    [Test]
    public async Task Filter_narrows_the_list_and_follows_the_user_across_projects()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string passes = $"Passes {suffix}", fails = $"Fails {suffix}", neverRun = $"Never run {suffix}",
            otherPasses = $"Other scope passes {suffix}", elsewhere = $"Elsewhere {suffix}";
        var scopeA = $"Scope A {suffix}";
        var scopeB = $"Scope B {suffix}";
        var viewerEmail = $"e2e-tc-filter-{suffix}@test.local";

        // Project 1: scope A = passed / failed / not run; scope B = one passed case.
        var dashboardUrl = await CreateProjectAsync($"E2E filter project {suffix}");
        var aCases = await CreateScopeWithCasesAsync(dashboardUrl, scopeA, passes, fails, neverRun);
        var bCases = await CreateScopeWithCasesAsync(dashboardUrl, scopeB, otherPasses);
        await MarkAsync(aCases[0], "Mark as passed");
        await MarkAsync(aCases[1], "Mark as failed");
        await MarkAsync(bCases[0], "Mark as passed");

        // Project 2: a single case that's never been run.
        var otherDashboardUrl = await CreateProjectAsync($"E2E filter other {suffix}");
        await CreateScopeWithCasesAsync(otherDashboardUrl, $"Scope C {suffix}", elsewhere);

        // A dedicated viewer, so their saved filter can't change any other test's view.
        await CreateUserAsync(viewerEmail, $"Filter viewer {suffix}", "Dev", DevPassword);
        await using var context = await Browser.NewContextAsync();
        var page = await SignInAsync(context, viewerEmail);
        var toggle = page.Locator("summary[aria-label='Filter test cases']");
        ILocator Case(string scenario) => page.GetByRole(AriaRole.Link, new() { Name = scenario, Exact = true });

        // Default: everything.
        await page.GotoAsync($"{dashboardUrl}/test-cases");
        await Expect(toggle).ToContainTextAsync("All test cases");
        foreach (var name in new[] { passes, fails, neverRun, otherPasses })
        {
            await Expect(Case(name)).ToBeVisibleAsync();
        }

        // Outstanding: the failed and never-run cases; scope B (all passed) drops out entirely.
        await PickAsync(page, "Outstanding");
        await Expect(Case(fails)).ToBeVisibleAsync();
        await Expect(Case(neverRun)).ToBeVisibleAsync();
        await Expect(Case(passes)).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = scopeB })).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-filter-summary]")).ToHaveTextAsync("Showing 2 of 4 test cases");

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await toggle.ClickAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, "test-case-filter.png") });
            await toggle.ClickAsync();
        }

        // A single result.
        await PickAsync(page, "Passed");
        await Expect(Case(passes)).ToBeVisibleAsync();
        await Expect(Case(otherPasses)).ToBeVisibleAsync();
        await Expect(Case(fails)).ToHaveCountAsync(0);

        // The choice is the user's, not the project's: project 2 opens filtered too, and
        // with nothing passed there it offers a way back to everything.
        await page.GotoAsync($"{otherDashboardUrl}/test-cases");
        await Expect(toggle).ToContainTextAsync("Passed");
        await Expect(page.GetByText("No test cases match “Passed”.")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Show all test cases" }).ClickAsync();
        await Expect(Case(elsewhere)).ToBeVisibleAsync();
        await Expect(toggle).ToContainTextAsync("All test cases");

        await DeleteUserAsync(viewerEmail);
    }

    private async Task MarkAsync(string caseUrl, string button)
    {
        await Page.GotoAsync(caseUrl);
        // Setting a result records the tester beside it.
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = button }).ClickAsync(),
            Page.Locator("[data-tested-by]"));
    }

    private async Task PickAsync(IPage page, string option)
    {
        await page.Locator("summary[aria-label='Filter test cases']").ClickAsync();
        await page.Locator("details[data-dropdown][open]")
            .GetByRole(AriaRole.Button, new() { Name = option }).First.ClickAsync();
        await Expect(page.Locator("summary[aria-label='Filter test cases']")).ToContainTextAsync(option);
    }
}
