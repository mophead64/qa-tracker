using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>In-app previews of attachments (images, video, audio, PDFs) in the shared modal.</summary>
[TestFixture]
public class AttachmentPreviewTests : E2ETestBase
{
    // A real 1×1 PNG, so the preview has to actually decode it.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj " +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF");

    [Test]
    public async Task Images_and_pdfs_open_in_the_preview_modal_other_files_stay_download_only()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E preview {Guid.NewGuid():N}");
        if (await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("Attachment storage is not configured on the target instance.");
        }

        var dir = Path.Combine(Path.GetTempPath(), $"preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, "screen.png");
        var pdf = Path.Combine(dir, "spec.pdf");
        var txt = Path.Combine(dir, "notes.txt");
        await File.WriteAllBytesAsync(png, Png);
        await File.WriteAllBytesAsync(pdf, Pdf);
        await File.WriteAllTextAsync(txt, "plain text");

        try
        {
            foreach (var path in new[] { png, pdf, txt })
            {
                await UploadAsync(path);
            }

            var modal = Page.Locator("#attachment-preview");
            var attachments = Page.Locator("#attachments");

            // Not previewable: no eye icon, the name is plain text.
            await Expect(attachments.GetByRole(AriaRole.Button, new() { Name = "Preview notes.txt" })).ToHaveCountAsync(0);
            await Expect(attachments.GetByRole(AriaRole.Button, new() { Name = "notes.txt", Exact = true })).ToHaveCountAsync(0);

            // Clicking the image's name opens it in the modal, and it really decodes.
            await attachments.GetByRole(AriaRole.Button, new() { Name = "screen.png", Exact = true }).ClickAsync();
            await Expect(modal).ToBeVisibleAsync();
            await Expect(modal.Locator("[data-preview-title]")).ToHaveTextAsync("screen.png");
            await Expect(modal.GetByRole(AriaRole.Button, new() { Name = "Close" })).ToBeFocusedAsync();
            var image = modal.Locator("img[data-preview-media='image']");
            await Expect(image).ToHaveAttributeAsync("src", new Regex(@"attachments/[0-9a-f-]{36}/preview$"));
            await Page.WaitForFunctionAsync(
                "() => { const i = document.querySelector('#attachment-preview img'); return i && i.complete && i.naturalWidth > 0; }");
            await Expect(modal.GetByRole(AriaRole.Link, new() { Name = "Download screen.png" }))
                .ToHaveAttributeAsync("href", new Regex(@"attachments/[0-9a-f-]{36}$"));

            if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } shots)
            {
                await Page.ScreenshotAsync(new() { Path = Path.Combine(shots, "attachment-preview.png") });
            }

            // Escape closes it and empties it.
            await Page.Keyboard.PressAsync("Escape");
            await Expect(modal).Not.ToBeVisibleAsync();
            await Expect(modal.Locator("[data-preview-media]")).ToHaveCountAsync(0);

            // The eye icon opens the PDF in a frame, served inline as a PDF.
            await attachments.GetByRole(AriaRole.Button, new() { Name = "Preview spec.pdf" }).ClickAsync();
            var frame = modal.Locator("iframe[data-preview-media='pdf']");
            await Expect(frame).ToBeVisibleAsync();
            var src = await frame.GetAttributeAsync("src");
            var response = await Page.APIRequest.GetAsync($"{BaseUrl}/{src}");
            Assert.That(response.Headers["content-type"], Does.StartWith("application/pdf"));
            Assert.That(response.Headers["content-disposition"], Does.StartWith("inline"));
            await modal.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
            await Expect(modal).Not.ToBeVisibleAsync();

            // A comment's image previews from its chip too.
            var note = $"See screenshot {Guid.NewGuid():N}";
            await SubmitUntil(
                async () =>
                {
                    await Page.GotoAsync(dashboardUrl);
                    await Page.GetByLabel("Add a comment").FillAsync(note);
                    await Page.Locator("[data-comment-file-input]").SetInputFilesAsync(png);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
                },
                Page.GetByText(note));
            await Page.Locator("#comments").GetByRole(AriaRole.Button, new() { Name = "Preview screen.png" }).ClickAsync();
            await Expect(modal.Locator("img[data-preview-media='image']")).ToBeVisibleAsync();
            await Expect(Page.Locator("#comments").GetByRole(AriaRole.Link, new() { Name = "Download screen.png" })).ToHaveCountAsync(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private async Task UploadAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        await RetryUntil(
            async () =>
            {
                try
                {
                    await Page.Locator("#attachments").GetByRole(AriaRole.Button, new() { Name = "Add attachments" })
                        .ClickAsync(new() { Timeout = 3000 });
                    await Page.Locator("dialog[open] input[type=file]").SetInputFilesAsync(filePath);
                    await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Upload" })
                        .ClickAsync(new() { Timeout = 3000 });
                }
                catch (TimeoutException)
                {
                    // Page still settling — try again.
                }
            },
            Page.GetByRole(AriaRole.Link, new() { Name = $"Download {fileName}" }),
            attempts: 30);
    }
}
