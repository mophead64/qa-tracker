using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for comments on the project dashboard: posting (with an
/// @-mention and, when storage is configured, a file), landing back on the Comments card,
/// deleting, and the mention notification linking back to the dashboard.</summary>
[TestFixture]
public class ProjectCommentTests : E2ETestBase
{
    private ILocator Comments => Page.Locator("#comments");

    private ILocator Box => Page.Locator("#new-comment");

    [Test]
    public async Task Comments_on_the_dashboard_mention_notify_and_delete()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var devName = $"Commentee {suffix}";
        var devEmail = $"e2e-project-comment-{suffix}@test.local";
        var tag = $"@{devName}";
        var fileName = $"e2e-project-comment-{suffix}.txt";
        var filePath = Path.Combine(Path.GetTempPath(), fileName);

        await CreateUserAsync(devEmail, devName, "Dev", DevPassword);
        var dashboardUrl = await CreateProjectAsync($"E2E project comments {suffix}");
        await AddToTeamAsync(dashboardUrl, devName);
        var storageConfigured = !await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync();

        await Expect(Comments.GetByText("No comments yet.")).ToBeVisibleAsync();

        await File.WriteAllTextAsync(filePath, "project comment attachment");
        try
        {
            // A comment mentioning the Dev (picked from the team list), plus a file when
            // storage is available. The post lands back on the Comments card.
            await SubmitUntil(
                async () =>
                {
                    await Box.FillAsync("");
                    await Box.PressSequentiallyAsync("Kick-off notes are in, ");
                    await Box.PressSequentiallyAsync("@Commentee");
                    await Box.PressAsync("Enter");
                    await Box.PressSequentiallyAsync("please review");
                    if (storageConfigured)
                    {
                        await Page.Locator("[data-comment-file-input]").SetInputFilesAsync(filePath);
                    }
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
                },
                Comments.Locator("li").Filter(new() { HasTextString = "Kick-off notes are in," }));
        }
        finally
        {
            File.Delete(filePath);
        }

        await Expect(Page).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}#comments$"));
        var mentionComment = Comments.Locator("li").Filter(new() { HasTextString = "Kick-off notes are in," });
        await Expect(mentionComment).ToContainTextAsync($"Kick-off notes are in, {tag} please review");
        await Expect(mentionComment.Locator("p span").Filter(new() { HasTextString = tag })).ToHaveTextAsync(tag);
        if (storageConfigured)
        {
            // The file belongs to the comment, not the project's Attachments panel.
            await Expect(mentionComment.GetByRole(AriaRole.Link, new() { Name = fileName })).ToBeVisibleAsync();
            await Expect(Page.GetByText("No attachments yet.")).ToBeVisibleAsync();
        }

        // A second, plain comment — then the author deletes it.
        var plain = $"Throwaway note {suffix}";
        await SubmitUntil(
            async () =>
            {
                await Box.FillAsync(plain);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            },
            Comments.GetByText(plain));
        await Comments.Locator("li").Filter(new() { HasTextString = plain })
            .GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Expect(Comments.GetByText(plain)).ToHaveCountAsync(0);
        await Expect(mentionComment).ToBeVisibleAsync();

        // The Dev gets a mention notification that opens the dashboard at the comments; they
        // can't delete someone else's comment.
        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);
        await devPage.Locator("summary[aria-label='Notifications']").ClickAsync();
        var notification = devPage.GetByText("mentioned you in a comment on the project dashboard");
        await Expect(notification).ToHaveCountAsync(1);
        await notification.ClickAsync();
        await Expect(devPage).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}#comments$"));
        var devView = devPage.Locator("#comments li").Filter(new() { HasTextString = "Kick-off notes are in," });
        await Expect(devView).ToBeVisibleAsync();
        await Expect(devView.GetByRole(AriaRole.Button, new() { Name = "Delete" })).ToHaveCountAsync(0);

        await DeleteUserAsync(devEmail);
    }
}
