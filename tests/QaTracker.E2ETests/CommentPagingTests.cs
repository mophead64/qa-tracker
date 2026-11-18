using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>Long comment threads: five per page, newest first, with the total and the older comments
/// signposted above the thread (CommentPagesBanner) as well as the pager below it.</summary>
[TestFixture]
public class CommentPagingTests : E2ETestBase
{
    private ILocator Items => Page.Locator("#comments ul > li");

    [Test]
    public async Task A_long_thread_shows_its_total_and_points_to_the_older_comments()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E comment pages {Guid.NewGuid():N}");
        await Page.GotoAsync(dashboardUrl);
        var banner = Page.Locator("[data-comment-pages]");

        for (var i = 1; i <= 7; i++)
        {
            var text = $"Paged comment {i:00}";
            await Page.Locator("#new-comment").FillAsync(text);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            await Expect(Page.Locator("#comments li").Filter(new() { HasTextString = text })).ToBeVisibleAsync();
            await Expect(Page.Locator("#new-comment")).ToHaveValueAsync("");
            if (i == 5)
            {
                // One page's worth: a count, but nothing to signpost yet.
                await Expect(Page.Locator("[data-comment-count]")).ToHaveTextAsync("5");
                await Expect(banner).ToHaveCountAsync(0);
            }
        }

        // Newest page: the last two, the total, and a way to the five older ones.
        await Expect(Page.Locator("[data-comment-count]")).ToHaveTextAsync("7");
        await Expect(Items).ToHaveCountAsync(2);
        await Expect(banner.Locator("[data-older-comments]")).ToHaveTextAsync("↑ Show 5 older comments");
        await Expect(banner).ToContainTextAsync("Showing the newest");

        // The older page, with the way back.
        await banner.Locator("[data-older-comments]").ClickAsync();
        await Expect(Items).ToHaveCountAsync(5);
        await Expect(Items.First).ToContainTextAsync("Paged comment 01");
        await Expect(banner).ToContainTextAsync("The first comments");
        await Expect(banner.Locator("[data-newest-comments]")).ToContainTextAsync("2 newer comments");

        await banner.Locator("[data-newest-comments]").ClickAsync();
        await Expect(Items).ToHaveCountAsync(2);
        await Expect(Items.Last).ToContainTextAsync("Paged comment 07");
    }
}
