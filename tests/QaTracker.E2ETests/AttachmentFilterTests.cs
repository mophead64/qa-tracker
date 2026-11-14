using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The attachments type filter beside the search box; stepping through previews with
/// Previous / Next (and the arrow keys) — across the list's pages, within the current filter; who
/// added each file; and comment files listed with the item's attachments, their comment shown in the preview.</summary>
[TestFixture]
public class AttachmentFilterTests : E2ETestBase
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj " +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF");

    private ILocator Rows => Page.Locator("#attachments > ul > li");

    private ILocator Modal => Page.Locator("#attachment-preview");

    [Test]
    public async Task Filter_by_type_and_step_through_previews()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E attachment filter {Guid.NewGuid():N}");
        if (await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("Attachment storage is not configured on the target instance.");
        }

        var dir = Path.Combine(Path.GetTempPath(), $"filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Six files — one more than a page, so the search and filter show and c.png lands on page 2.
            var files = new (string Name, byte[] Bytes)[]
            {
                ("notes.txt", "plain text"u8.ToArray()), ("bundle.zip", [80, 75, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
                ("a.png", Png), ("b.png", Png), ("spec.pdf", Pdf), ("c.png", Png),
            };
            foreach (var (name, bytes) in files)
            {
                var path = Path.Combine(dir, name);
                await File.WriteAllBytesAsync(path, bytes);
                await UploadAsync(path, name == "b.png" ? "Basket after applying the code" : null);
            }

            // Every previewable file steps in turn, across pages: a.png (page 1) … c.png (page 2).
            await Page.GotoAsync(dashboardUrl);
            await Expect(Rows).ToHaveCountAsync(5);
            await Page.Locator("#attachments").GetByRole(AriaRole.Button, new() { Name = "a.png", Exact = true }).ClickAsync();
            await ExpectShowingAsync("a.png", "1 of 4");
            await Expect(Modal.Locator("[data-preview-meta]")).ToHaveTextAsync(new Regex(@"^Added by .+ on \d{4}-\d{2}-\d{2} \d{2}:\d{2} UTC$"));
            await Expect(Modal.Locator("[data-preview-comment]")).ToBeHiddenAsync();
            await Expect(Modal.Locator("[data-preview-description]")).ToBeHiddenAsync();
            await Expect(Modal.Locator("[data-preview-prev]")).ToBeDisabledAsync();
            await Modal.Locator("[data-preview-next]").ClickAsync();
            await ExpectShowingAsync("b.png", "2 of 4");
            await Expect(Modal.Locator("[data-preview-description-text]")).ToHaveTextAsync("Basket after applying the code");
            await Page.Keyboard.PressAsync("ArrowRight");
            await ExpectShowingAsync("spec.pdf", "3 of 4");
            await Expect(Modal.Locator("[data-preview-description]")).ToBeHiddenAsync(); // cleared on stepping on
            await Page.Keyboard.PressAsync("ArrowRight");
            await ExpectShowingAsync("c.png", "4 of 4");
            await Expect(Modal.Locator("[data-preview-next]")).ToBeDisabledAsync();
            await Page.Keyboard.PressAsync("ArrowLeft");
            await ExpectShowingAsync("spec.pdf", "3 of 4");
            await Page.Keyboard.PressAsync("Escape");

            // Filter to images: three rows, the toggle names the filter, and stepping stays within it.
            var filter = Page.Locator("[data-attachment-type-filter]");
            await filter.Locator("summary").ClickAsync();
            await Expect(filter.Locator("[data-attachment-type='image']")).ToContainTextAsync("3");
            await Expect(filter.Locator("[data-attachment-type='docs']")).ToContainTextAsync("2");
            await Expect(filter.Locator("[data-attachment-type='other']")).ToContainTextAsync("1");
            await filter.Locator("[data-attachment-type='image']").ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"attachmentType=image"));
            await Expect(Rows).ToHaveCountAsync(3);
            await Expect(filter.Locator("[data-attachment-type-current]")).ToHaveTextAsync("Images");
            await Page.Locator("#attachments").GetByRole(AriaRole.Button, new() { Name = "Preview b.png" }).ClickAsync();
            await ExpectShowingAsync("b.png", "2 of 3");
            await Page.Keyboard.PressAsync("Escape");

            // Docs and Other; then All again.
            await filter.Locator("summary").ClickAsync();
            await filter.Locator("[data-attachment-type='docs']").ClickAsync();
            await Expect(Rows).ToHaveCountAsync(2);
            await Expect(Rows.Filter(new() { HasTextString = "notes.txt" })).ToHaveCountAsync(1);
            await Expect(Rows.Filter(new() { HasTextString = "spec.pdf" })).ToHaveCountAsync(1);
            await filter.Locator("summary").ClickAsync();
            await filter.Locator("[data-attachment-type='other']").ClickAsync();
            await Expect(Rows).ToHaveCountAsync(1);
            await Expect(Rows).ToContainTextAsync("bundle.zip");
            await filter.Locator("summary").ClickAsync();
            await filter.Locator("[data-attachment-type='all']").ClickAsync();
            await Expect(Rows).ToHaveCountAsync(5);
            await Expect(filter.Locator("[data-attachment-type-current]")).ToHaveCountAsync(0);

            // A single previewable file has no stepping controls.
            await filter.Locator("summary").ClickAsync();
            await filter.Locator("[data-attachment-type='docs']").ClickAsync();
            await Page.Locator("#attachments").GetByRole(AriaRole.Button, new() { Name = "Preview spec.pdf" }).ClickAsync();
            await Expect(Modal.Locator("[data-preview-title]")).ToHaveTextAsync("spec.pdf");
            await Expect(Modal.Locator("[data-preview-nav]")).ToBeHiddenAsync();
            await Page.Keyboard.PressAsync("Escape");

            // A file posted with a comment (mentioning a teammate) joins the list, marked with its comment…
            await AddSelfToTeamAsync(dashboardUrl);
            var me = await CurrentUserNameAsync();
            var note = $"Repro capture {Guid.NewGuid():N} for @{me}";
            var dPng = Path.Combine(dir, "d.png");
            await File.WriteAllBytesAsync(dPng, Png);
            await Page.GotoAsync(dashboardUrl);
            await Page.Locator("#new-comment").FillAsync(note);
            await Page.Locator("[data-comment-file-input]").SetInputFilesAsync(dPng);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            await Expect(Page.Locator("#comments li").Filter(new() { HasTextString = note })).ToBeVisibleAsync();

            await filter.Locator("summary").ClickAsync();
            await filter.Locator("[data-attachment-type='image']").ClickAsync();
            await Expect(Rows).ToHaveCountAsync(4);
            var fromComment = Rows.Filter(new() { HasTextString = "d.png" });
            await Expect(fromComment.Locator("[data-attachment-comment]")).ToContainTextAsync("With a comment by");
            await Expect(fromComment.Locator("summary[aria-label='Delete d.png']")).ToHaveCountAsync(0); // goes with its comment

            // …and its preview shows that comment, with a way to it.
            await Page.Locator("#attachments").GetByRole(AriaRole.Button, new() { Name = "Preview d.png" }).ClickAsync();
            await ExpectShowingAsync("d.png", "4 of 4");
            await Expect(Modal.Locator("[data-preview-comment]")).ToBeVisibleAsync();
            await Expect(Modal.Locator("[data-preview-comment-body]")).ToHaveTextAsync(note);
            // …its @mention tagged green, as under the comment…
            await Expect(Modal.Locator("[data-preview-comment-body] span")).ToHaveTextAsync("@" + me);
            await Expect(Modal.Locator("[data-preview-comment-body] span")).ToHaveClassAsync(new Regex("bg-brand-100"));
            // …and Go to comment lands on it, outlined.
            await Modal.Locator("[data-preview-comment-link]").ClickAsync();
            await Expect(Modal).Not.ToBeVisibleAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"commentsPage=1&highlight=[0-9a-f-]{36}#comment-[0-9a-f-]{36}$"));
            await Expect(Page.Locator("#comments li.comment-highlight")).ToHaveCountAsync(1);
            await Expect(Page.Locator("#comments li.comment-highlight")).ToContainTextAsync(note);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private async Task ExpectShowingAsync(string name, string position)
    {
        await Expect(Modal.Locator("[data-preview-title]")).ToHaveTextAsync(name);
        await Expect(Modal.Locator("[data-preview-position]")).ToHaveTextAsync(position);
    }

    private async Task UploadAsync(string filePath, string? description = null)
    {
        var fileName = Path.GetFileName(filePath);
        await RetryUntil(
            async () =>
            {
                try
                {
                    await Page.Locator("#attachments [data-upload-open]").ClickAsync(new() { Timeout = 3000 });
                    await Page.Locator("dialog[open] input[type=file]").SetInputFilesAsync(filePath);
                    if (description is not null)
                    {
                        await Page.Locator("dialog[open] [data-upload-description]").FillAsync(description);
                    }
                    await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Upload" })
                        .ClickAsync(new() { Timeout = 3000 });
                }
                catch (TimeoutException)
                {
                    // Page still settling — try again.
                }
            },
            // The newest file can land on page 2 once there are more than five; the upload panel
            // returns to page 1, so probe the count line instead of the row.
            Page.Locator("#attachments").GetByText(fileName).Or(Page.Locator("#attachments nav[aria-label='Attachments pages']")).First,
            attempts: 30);
    }
}
