using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The project's activity: the dashboard's lazily loaded Recent activity card and the
/// full activity page with its filters and "Load more".</summary>
[TestFixture]
public class ProjectActivityTests : E2ETestBase
{
    private ILocator RecentActivity => Page.Locator("[data-recent-activity]");

    [Test]
    public async Task Dashboard_card_shows_the_latest_test_case_and_project_changes()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E activity project {Guid.NewGuid():N}");
        var scope = $"Activity scope {Guid.NewGuid():N}";
        var scenario = $"Activity case {Guid.NewGuid():N}";
        await CreateScopeWithCasesAsync(dashboardUrl, scope, scenario);
        var me = await CurrentUserNameAsync();

        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Mark as passed" }).ClickAsync(),
            Page.Locator("summary[aria-label='Change result']", new() { HasTextString = "Passed" }));

        await Page.GotoAsync(dashboardUrl);
        var items = RecentActivity.Locator("[data-history] > li");
        await Expect(items).ToHaveCountAsync(5);
        await Expect(items.Nth(0)).ToContainTextAsync(new Regex($"{Regex.Escape(me)}\\s+marked T-\\d+ {Regex.Escape(scenario)} as Passed"));
        await Expect(items.Nth(1)).ToContainTextAsync(new Regex($"{Regex.Escape(me)}\\s+created test case T-\\d+ {Regex.Escape(scenario)}"));
        await Expect(items.Nth(2)).ToContainTextAsync($"{me} moved the project from Not Started to Active");
        await Expect(items.Nth(3)).ToContainTextAsync(new Regex($"{Regex.Escape(me)}\\s+created scope S-\\d+ {Regex.Escape(scope)}"));
        await Expect(items.Nth(4)).ToContainTextAsync($"{me} created the project");

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.Locator("#activity").ScrollIntoViewIfNeededAsync();
            await Page.Locator("#activity").ScreenshotAsync(new() { Path = Path.Combine(dir, "activity-card.png") });
        }

        // Entries link to what they're about.
        await items.Nth(0).GetByRole(AriaRole.Link, new() { Name = scenario }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = scenario })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Activity_page_filters_and_loads_more()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E activity paging {Guid.NewGuid():N}");
        var status = Page.Locator("summary[aria-label='Change project status']");

        // 25 status changes + "created the project" = 26 entries: one more than a page.
        string[] labels = ["Active", "Not Started"];
        for (var i = 0; i < 25; i++)
        {
            var label = labels[i % 2];
            await RetryUntil(
                async () =>
                {
                    await status.ClickAsync();
                    await Page.GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync();
                },
                status.Filter(new() { HasTextString = label }));
        }

        await Page.GetByRole(AriaRole.Link, new() { Name = "View all activity" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/activity$"));
        var feed = Page.Locator("[data-activity-feed]");
        var items = feed.Locator("[data-history] > li");
        await Expect(items).ToHaveCountAsync(25);
        await Expect(items.Nth(0)).ToContainTextAsync("moved the project from Not Started to Active");

        // "Load more" appends the next page in place and goes once there's nothing older.
        var more = feed.GetByRole(AriaRole.Link, new() { Name = "Load more" });
        var olderPageHref = await more.GetAttributeAsync("href");
        await more.ClickAsync();
        await Expect(items).ToHaveCountAsync(26);
        await Expect(items.Nth(25)).ToContainTextAsync("created the project");
        await Expect(feed.GetByRole(AriaRole.Link, new() { Name = "Load more" })).ToHaveCountAsync(0);
        await Expect(Page).ToHaveURLAsync(new Regex("/activity$"));

        // Without JS the link opens the older page on its own.
        await Page.GotoAsync($"{BaseUrl}/{olderPageHref}");
        await Expect(items).ToHaveCountAsync(1);
        await Expect(items.Nth(0)).ToContainTextAsync("created the project");
        await Expect(feed.GetByRole(AriaRole.Link, new() { Name = "Back to the newest" })).ToBeVisibleAsync();

        // Filters narrow the feed.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Show activity for" })
            .GetByRole(AriaRole.Link, new() { Name = "Test cases", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/activity\\?show=test-cases$"));
        await Expect(feed).ToContainTextAsync("No test case activity yet.");
    }
}
