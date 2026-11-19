using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The shared comment box: layout, ⌘/Ctrl+Enter to post, and pasting screenshots as attachments.</summary>
[TestFixture]
public class CommentComposerTests : E2ETestBase
{
    [Test]
    public async Task Ctrl_or_cmd_enter_posts_and_the_button_shares_the_attachments_row()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E composer {Guid.NewGuid():N}");
        var form = Page.Locator("[data-comment-form]");
        var box = Page.GetByLabel("Add a comment");
        var submit = form.GetByRole(AriaRole.Button, new() { Name = "Add comment" });

        // The shortcut hint sits on the button; its accessible name stays "Add comment".
        await Expect(submit.Locator("[data-shortcut-hint]")).ToHaveTextAsync(new Regex("^(⌘↵|Ctrl ↵)$"));
        await Expect(submit).ToHaveAccessibleNameAsync("Add comment");

        // With storage: [Add attachments] · limits … [Add comment] on one row, button on the right.
        if (await form.Locator("[data-comment-file]").CountAsync() > 0)
        {
            var attach = (await form.GetByText("Add attachments").BoundingBoxAsync())!;
            var button = (await submit.BoundingBoxAsync())!;
            Assert.That(Math.Abs((attach.Y + attach.Height / 2) - (button.Y + button.Height / 2)), Is.LessThan(8),
                "Add comment should sit on the Add attachments row");
            Assert.That(button.X, Is.GreaterThan(attach.X + attach.Width));
            await Expect(form.Locator("[data-comment-file-hint]")).ToHaveTextAsync(new Regex(@"^Optional · up to \d+ · max \d+ MB · or paste$"));
        }

        // Empty box: the shortcut doesn't post (the box is required).
        await box.FocusAsync();
        await box.PressAsync("Control+Enter");
        await Expect(Page.Locator("#comments li[id^='comment-']")).ToHaveCountAsync(0);

        // Ctrl+Enter posts.
        var first = $"Posted with Ctrl+Enter {Guid.NewGuid():N}";
        await box.FillAsync(first);
        await box.PressAsync("Control+Enter");
        await Expect(Page.GetByText(first)).ToBeVisibleAsync();

        // ⌘+Enter (Meta) posts too.
        var second = $"Posted with Cmd+Enter {Guid.NewGuid():N}";
        await Page.GetByLabel("Add a comment").FillAsync(second);
        await Page.GetByLabel("Add a comment").PressAsync("Meta+Enter");
        await Expect(Page.GetByText(second)).ToBeVisibleAsync();

        // Plain Enter is still a newline, not a post.
        await Page.GetByLabel("Add a comment").FillAsync("line one");
        await Page.GetByLabel("Add a comment").PressAsync("Enter");
        await Expect(Page.GetByLabel("Add a comment")).ToHaveValueAsync("line one\n");
        await Expect(Page.Locator("#comments li[id^='comment-']")).ToHaveCountAsync(2);

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await form.ScrollIntoViewIfNeededAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "comment-composer.png") });
        }
    }

    /// <summary>Fires a paste on the comment box carrying <paramref name="count"/> PNGs named "image.png",
    /// as a screenshot on the clipboard does.</summary>
    private Task PasteScreenshotsAsync(int count) => Page.GetByLabel("Add a comment").EvaluateAsync(@"(box, count) => {
        const png = Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='), c => c.charCodeAt(0));
        const dt = new DataTransfer();
        for (let i = 0; i < count; i++) dt.items.add(new File([png], 'image.png', { type: 'image/png' }));
        box.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }));
    }", count);

    [Test]
    public async Task Pasting_a_screenshot_attaches_it_to_the_comment()
    {
        await CreateProjectAsync($"E2E paste {Guid.NewGuid():N}");
        var form = Page.Locator("[data-comment-form]");
        if (await form.Locator("[data-comment-file]").CountAsync() == 0)
        {
            Assert.Ignore("File storage isn't configured, so comments take no attachments.");
        }

        var note = $"Pasted from the clipboard {Guid.NewGuid():N}";
        await Page.GetByLabel("Add a comment").FillAsync(note);
        await PasteScreenshotsAsync(1);

        // Listed like a picked file, with a timestamped name instead of "image.png"; the text is untouched.
        var listed = form.Locator("[data-comment-file-list] li");
        await Expect(listed).ToHaveCountAsync(1);
        await Expect(listed.First).ToContainTextAsync(new Regex(@"screenshot-\d{4}-\d{2}-\d{2}-\d{6}\.png"));
        await Expect(Page.GetByLabel("Add a comment")).ToHaveValueAsync(note);

        await form.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
        var posted = Page.Locator("#comments li").Filter(new() { HasTextString = note });
        await Expect(posted.GetByRole(AriaRole.Link, new() { NameRegex = new Regex(@"screenshot-.*\.png") })).ToBeVisibleAsync();

        // The per-comment limit applies to pastes too.
        await PasteScreenshotsAsync(11);
        await Expect(form.Locator("[data-comment-file-list] li")).ToHaveCountAsync(10);
        await Expect(form.Locator("[data-comment-file-error]")).ToContainTextAsync("at most 10 attachments");
    }
}
