using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The History modal on the defect page: transitions, assignment and edits, and who made them.</summary>
[TestFixture]
public class DefectHistoryTests : E2ETestBase
{
    [Test]
    public async Task History_shows_severity_status_edits_and_assignment_newest_first()
    {
        var summary = $"History defect {Guid.NewGuid():N}";
        var dashboardUrl = await CreateProjectAsync($"E2E defect history {Guid.NewGuid():N}");
        var me = await CurrentUserNameAsync();
        await AddSelfToTeamAsync(dashboardUrl); // assignees are picked from the project team

        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));
        var defectUrl = Page.Url;

        // Severity Medium → High, then status Not fixed → Fixing.
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change severity']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "High", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change severity']").Filter(new() { HasTextString = "High" }));
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Fixing", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change status']").Filter(new() { HasTextString = "Fixing" }));

        // One edit-form save that changes a field and takes the defect.
        await SubmitUntil(
            async () =>
            {
                await Page.GotoAsync($"{defectUrl}/edit");
                await Page.GetByLabel("Actual results").FillAsync("Nothing happens");
                await PickFormSelectAsync("Assigned to", $"{me} (QA)");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change assignee']").Filter(new() { HasTextString = me }));

        var modal = Page.Locator("#history-modal");
        await Expect(modal).Not.ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(modal).ToBeVisibleAsync();

        var items = modal.Locator("[data-history] > li");
        await Expect(items).ToHaveCountAsync(5);
        // The edit save: assignment and field edit share an instant; the assignment reads first.
        await Expect(items.Nth(0)).ToContainTextAsync($"{me} assigned it to themselves");
        await Expect(items.Nth(1)).ToContainTextAsync($"{me} edited the actual results");
        await Expect(items.Nth(2)).ToContainTextAsync($"{me} moved it from Not fixed to Fixing");
        await Expect(items.Nth(3)).ToContainTextAsync($"{me} changed the severity from Medium to High");
        await Expect(items.Nth(4)).ToContainTextAsync($"{me} raised this defect");
        await Expect(items.Nth(2)).ToHaveAttributeAsync("data-history-kind", "StatusChanged");
        await Expect(items.Nth(0).Locator("time")).ToHaveTextAsync(new Regex("^(just now|\\d+ minutes? ago)$"));

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "defect-history.png") });
        }

        await modal.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await Expect(modal).Not.ToBeVisibleAsync();
    }
}
