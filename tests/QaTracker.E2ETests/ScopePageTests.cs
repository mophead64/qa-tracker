using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The single-scope page (click a scope's name) and the expanded / collapsed layout.</summary>
[TestFixture]
public class ScopePageTests : E2ETestBase
{
    [Test]
    public async Task Scope_page_and_collapsed_layout()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string passes = $"Passes {suffix}", fails = $"Fails {suffix}", neverRun = $"Never run {suffix}",
            otherPasses = $"Other scope passes {suffix}";
        var scopeA = $"Scope A {suffix}";
        var scopeB = $"Scope B {suffix}";
        var viewerEmail = $"e2e-scope-page-{suffix}@test.local";

        // Scope A = passed / failed / not run; scope B = one passed case.
        var dashboardUrl = await CreateProjectAsync($"E2E scope page {suffix}");
        var aCases = await CreateScopeWithCasesAsync(dashboardUrl, scopeA, passes, fails, neverRun);
        var bCases = await CreateScopeWithCasesAsync(dashboardUrl, scopeB, otherPasses);
        await MarkAsync(aCases[0], "Mark as passed");
        await MarkAsync(aCases[1], "Mark as failed");
        await MarkAsync(bCases[0], "Mark as passed");

        // The test case breadcrumb's scope name now links to the scope page.
        await Page.GotoAsync(aCases[0]);
        await Page.Locator("p").GetByRole(AriaRole.Link, new() { Name = scopeA, Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases/scopes/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = scopeA })).ToBeVisibleAsync();

        // A dedicated QA viewer, so their saved layout/filter can't change any other test's view.
        await CreateUserAsync(viewerEmail, $"Scope viewer {suffix}", "QA", DevPassword);
        await using var context = await Browser.NewContextAsync();
        var page = await SignInAsync(context, viewerEmail);
        ILocator Case(string scenario) => page.GetByRole(AriaRole.Link, new() { Name = scenario, Exact = true });
        var listUrl = $"{dashboardUrl}/test-cases";

        // Clicking a scope's name opens just that scope.
        await page.GotoAsync(listUrl);
        await page.Locator("section[id^='scope-'] h2").GetByRole(AriaRole.Link, new() { Name = scopeA, Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/test-cases/scopes/[0-9a-fA-F-]{36}$"));
        var scopeUrl = page.Url;
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = scopeA })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-scope-tally]")).ToHaveTextAsync("1 passed · 1 failed · 1 not run");
        foreach (var name in new[] { passes, fails, neverRun })
        {
            await Expect(Case(name)).ToBeVisibleAsync();
        }
        await Expect(Case(otherPasses)).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Expanded" })).ToHaveCountAsync(0);

        // Same filter here, and the quick actions come back to the scope page.
        await PickFilterAsync(page, "Outstanding");
        await Expect(page).ToHaveURLAsync(scopeUrl);
        await Expect(Case(passes)).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-filter-summary]")).ToHaveTextAsync("Showing 2 of 3 test cases");
        await page.Locator("li").Filter(new() { Has = Case(fails) })
            .GetByRole(AriaRole.Button, new() { Name = "Pass", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(scopeUrl);
        await Expect(Case(fails)).ToHaveCountAsync(0);     // now passed, so filtered out
        await Expect(Case(neverRun)).ToBeVisibleAsync();
        await PickFilterAsync(page, "All test cases");

        // Collapsed: one row per scope with its tally and a result bar, no case rows; it sticks.
        await page.GotoAsync(listUrl);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Expanded" })).ToHaveAttributeAsync("aria-pressed", "true");
        await page.GetByRole(AriaRole.Button, new() { Name = "Collapsed" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Collapsed" })).ToHaveAttributeAsync("aria-pressed", "true");
        var rows = page.Locator("[data-scope-row]");
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(rows.Nth(0)).ToContainTextAsync(scopeA);
        await Expect(rows.Nth(0).Locator("[data-scope-tally]")).ToHaveTextAsync("2 passed · 0 failed · 1 not run");
        await Expect(rows.Nth(0).Locator("[data-result-bar]")).ToBeVisibleAsync();
        await Expect(Case(neverRun)).ToHaveCountAsync(0);

        await page.ReloadAsync();
        await Expect(rows).ToHaveCountAsync(2);

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, "collapsed.png") });
        }

        // The collapsed row's name opens the scope page too.
        await rows.Nth(1).GetByRole(AriaRole.Link, new() { Name = scopeB, Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = scopeB })).ToBeVisibleAsync();
        await Expect(Case(otherPasses)).ToBeVisibleAsync();

        // Reset the case the viewer passed: "Tested by" is a restricted FK, so it would block
        // deleting the throwaway account.
        await page.GotoAsync(scopeUrl);
        await page.Locator("li").Filter(new() { Has = Case(fails) })
            .GetByRole(AriaRole.Button, new() { Name = "Reset", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-scope-tally]")).ToHaveTextAsync("1 passed · 0 failed · 2 not run");

        await DeleteUserAsync(viewerEmail);
    }

    private async Task MarkAsync(string caseUrl, string button)
    {
        await Page.GotoAsync(caseUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = button }).ClickAsync(),
            Page.Locator("[data-tested-by]"));
    }

    private async Task PickFilterAsync(IPage page, string option)
    {
        await page.Locator("summary[aria-label='Filter test cases']").ClickAsync();
        await page.Locator("details[data-dropdown][open]")
            .GetByRole(AriaRole.Button, new() { Name = option }).First.ClickAsync();
        await Expect(page.Locator("summary[aria-label='Filter test cases']")).ToContainTextAsync(option);
    }
}
