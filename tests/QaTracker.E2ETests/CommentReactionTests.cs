using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for thumbs up / down on comments: counts, toggling off,
/// switching, the "who reacted" tooltip text, a second user, and landing back on the comment.</summary>
[TestFixture]
public class CommentReactionTests : E2ETestBase
{
    private static ILocator Up(ILocator comment) => comment.Locator("[data-reaction=Up]");

    private static ILocator Down(ILocator comment) => comment.Locator("[data-reaction=Down]");

    private async Task ExpectCounts(ILocator comment, int up, int down)
    {
        await Expect(Up(comment).Locator("[data-reaction-count]")).ToHaveTextAsync(up.ToString());
        await Expect(Down(comment).Locator("[data-reaction-count]")).ToHaveTextAsync(down.ToString());
    }

    [Test]
    public async Task Thumbs_up_and_down_on_a_dashboard_comment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var devName = $"Reactor {suffix}";
        var devEmail = $"e2e-reaction-{suffix}@test.local";
        var note = $"Reaction target {suffix}";

        await CreateUserAsync(devEmail, devName, "Dev", DevPassword);
        var dashboardUrl = await CreateProjectAsync($"E2E reactions {suffix}");
        await AddToTeamAsync(dashboardUrl, devName);

        await SubmitUntil(
            async () =>
            {
                await Page.Locator("#new-comment").FillAsync(note);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            },
            Page.Locator("#comments li").Filter(new() { HasTextString = note }));
        var comment = Page.Locator("#comments li").Filter(new() { HasTextString = note });
        await ExpectCounts(comment, 0, 0);
        await Expect(Up(comment)).ToHaveAttributeAsync("title", "Thumbs up");

        // Thumbs up: counted, pressed, and the page comes back to this comment.
        await Up(comment).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("#comment-[0-9a-fA-F-]{36}$"));
        await ExpectCounts(comment, 1, 0);
        await Expect(Up(comment)).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Up(comment)).ToHaveAttributeAsync("title", "Thumbs up: You");

        // The same again takes it back; the other one switches.
        await Up(comment).ClickAsync();
        await ExpectCounts(comment, 0, 0);
        await Expect(Up(comment)).ToHaveAttributeAsync("aria-pressed", "false");
        await Up(comment).ClickAsync();
        await Down(comment).ClickAsync();
        await ExpectCounts(comment, 0, 1);
        await Expect(Down(comment)).ToHaveAttributeAsync("aria-pressed", "true");

        // A second user's reaction adds to the counts, and each sees who reacted.
        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);
        await devPage.GotoAsync(dashboardUrl);
        var devComment = devPage.Locator("#comments li").Filter(new() { HasTextString = note });
        await Expect(Down(devComment)).ToHaveAttributeAsync("aria-pressed", "false");
        await Up(devComment).ClickAsync();
        await ExpectCounts(devComment, 1, 1);
        await Expect(Up(devComment)).ToHaveAttributeAsync("title", "Thumbs up: You");

        await Page.ReloadAsync();
        await ExpectCounts(comment, 1, 1);
        await Expect(Up(comment)).ToHaveAttributeAsync("title", $"Thumbs up: {devName}");
        await Expect(Down(comment)).ToHaveAttributeAsync("title", "Thumbs down: You");

        // The comment's author is told about the other user's reaction; the notification
        // opens the dashboard at its comments.
        await Page.Locator("summary[aria-label='Notifications']").ClickAsync();
        var notification = Page.GetByText($"{devName} gave your comment a thumbs up on the project dashboard");
        await Expect(notification).ToHaveCountAsync(1);
        await notification.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}#comments$"));

        await DeleteUserAsync(devEmail);
    }

    [Test]
    public async Task Thumbs_up_on_a_defect_comment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var summary = $"Reaction defect {suffix}";
        var note = $"Defect reaction target {suffix}";

        var dashboardUrl = await CreateProjectAsync($"E2E defect reactions {suffix}");
        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));

        await SubmitUntil(
            async () =>
            {
                await Page.Locator("#new-comment").FillAsync(note);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            },
            Page.Locator("li").Filter(new() { HasTextString = note }));
        var comment = Page.Locator("li[id^=comment-]").Filter(new() { HasTextString = note });

        await Up(comment).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/defects/[0-9a-fA-F-]{36}#comment-[0-9a-fA-F-]{36}$"));
        await ExpectCounts(comment, 1, 0);
        await Expect(Up(comment)).ToHaveAttributeAsync("aria-pressed", "true");
    }
}
