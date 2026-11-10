using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The History timeline on the test case page: who created, edited and ran the case.</summary>
[TestFixture]
public class TestCaseHistoryTests : E2ETestBase
{
    [Test]
    public async Task History_shows_creation_edits_and_result_changes_newest_first()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E history project {Guid.NewGuid():N}");
        var scenario = $"History case {Guid.NewGuid():N}";
        await CreateScopeWithCasesAsync(dashboardUrl, $"History scope {Guid.NewGuid():N}", scenario);
        var me = await CurrentUserNameAsync();

        // Not run → Passed (the quick button) → Failed (the result menu).
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Mark as passed" }).ClickAsync(),
            Page.Locator("summary[aria-label='Change result']", new() { HasTextString = "Passed" }));
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change result']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Failed", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change result']", new() { HasTextString = "Failed" }));

        // Editing only the steps is recorded as a steps edit.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Steps").FillAsync("1. Open the app\n2. Sign in");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = scenario }));

        // History is a modal opened from the button beside Edit.
        var modal = Page.Locator("#history-modal");
        await Expect(modal).Not.ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(modal).ToBeVisibleAsync();

        var items = modal.Locator("[data-history] > li");
        await Expect(items).ToHaveCountAsync(4);
        await Expect(items.Nth(0)).ToContainTextAsync($"{me} edited the steps");
        await Expect(items.Nth(1)).ToContainTextAsync($"{me} changed the result from Passed to Failed");
        await Expect(items.Nth(2)).ToContainTextAsync($"{me} marked it as Passed");
        await Expect(items.Nth(3)).ToContainTextAsync($"{me} created this test case");

        // Everything happened moments ago, so it reads relatively; the exact time is in the tooltip.
        await Expect(items.Nth(0).Locator("time")).ToHaveTextAsync(new Regex("^(just now|\\d+ minutes? ago)$"));
        await Expect(items.Nth(0).Locator("time")).ToHaveAttributeAsync("title", new Regex("UTC$"));

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "history.png") });
        }

        await modal.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await Expect(modal).Not.ToBeVisibleAsync();
    }
}
