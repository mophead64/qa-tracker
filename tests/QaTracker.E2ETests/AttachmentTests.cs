using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// End-to-end coverage for uploading a file on a project, a test case, and a
/// defect, and downloading it back. Self-skips (in addition to the base class's checks)
/// when the target instance has no storage provider configured — the dashboard's
/// Attachments card shows "Attachment storage isn't configured" in that case.
/// </summary>
[TestFixture]
public class AttachmentTests : E2ETestBase
{
    [Test]
    public async Task Comment_on_a_defect_can_carry_optional_files()
    {
        var projectName = $"E2E comment file {Guid.NewGuid():N}";
        var summary = $"Crash on save {Guid.NewGuid():N}";
        var note = $"Screenshot attached {Guid.NewGuid():N}";
        var plainNote = $"No file on this one {Guid.NewGuid():N}";

        await CreateProjectAsync(projectName);

        if (await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("Attachment storage is not configured on the target instance.");
        }

        await Page.GetByRole(AriaRole.Link, new() { Name = "Defects" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "New defect" }).First.ClickAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByLabel("Repro steps").FillAsync("1. Open the page\n2. Save");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));

        var fileNames = new[] { $"e2e-comment-a-{Guid.NewGuid():N}.txt", $"e2e-comment-b-{Guid.NewGuid():N}.txt" };
        var filePaths = fileNames.Select(n => Path.Combine(Path.GetTempPath(), n)).ToArray();
        foreach (var path in filePaths)
        {
            await File.WriteAllTextAsync(path, "comment attachment content");
        }

        try
        {
            // A comment with two files: each appears as a download chip under the comment.
            await SubmitUntil(
                async () =>
                {
                    await Page.GetByLabel("Add a comment").FillAsync(note);
                    await Page.Locator("[data-comment-file-input]").SetInputFilesAsync(filePaths);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
                },
                Page.GetByText(note));
            foreach (var fileName in fileNames)
            {
                await Expect(Page.GetByRole(AriaRole.Link, new() { Name = fileName })).ToBeVisibleAsync();
            }

            // The file belongs to the comment, so it isn't listed in the defect's Attachments panel.
            await Expect(Page.GetByText("No attachments yet.")).ToBeVisibleAsync();

            // A comment without a file still works.
            await SubmitUntil(
                async () =>
                {
                    await Page.GetByLabel("Add a comment").FillAsync(plainNote);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
                },
                Page.GetByText(plainNote));
        }
        finally
        {
            foreach (var path in filePaths)
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task Qa_can_upload_a_file_on_a_project_a_test_case_and_a_defect()
    {
        var projectName = $"E2E attachments {Guid.NewGuid():N}";
        var scopeName = $"Uploads {Guid.NewGuid():N}";
        var scenario = $"When a user attaches a screenshot {Guid.NewGuid():N}";
        var defectSummary = $"Upload button does nothing {Guid.NewGuid():N}";

        var dashboardUrl = await CreateProjectAsync(projectName);

        if (await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("Attachment storage is not configured on the target instance.");
        }

        var filePath = Path.Combine(Path.GetTempPath(), $"e2e-attachment-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(filePath, "sample attachment content");

        try
        {
            // Project-level attachment, on the dashboard we already landed on.
            await UploadOnCurrentPageAsync(filePath);

            // Test-case attachment.
            await Page.GetByRole(AriaRole.Link, new() { Name = "Test cases" }).First.ClickAsync();
            await Page.GetByRole(AriaRole.Link, new() { Name = "New scope" }).ClickAsync();
            await SubmitUntil(
                async () =>
                {
                    await Page.GetByLabel("Name").FillAsync(scopeName);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Create scope" }).ClickAsync();
                },
                Page.GetByRole(AriaRole.Heading, new() { Name = scopeName }));

            await Page.GetByRole(AriaRole.Link, new() { Name = "New test case" }).ClickAsync();
            await SubmitUntil(
                async () =>
                {
                    await Page.GetByLabel("Scenario").FillAsync(scenario);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Create test case" }).ClickAsync();
                },
                Page.GetByRole(AriaRole.Heading, new() { Name = scenario }));

            await UploadOnCurrentPageAsync(filePath);

            // Defect file.
            await Page.GotoAsync(dashboardUrl);
            await Page.GetByRole(AriaRole.Link, new() { Name = "Defects" }).First.ClickAsync();
            await Page.GetByRole(AriaRole.Link, new() { Name = "New defect" }).First.ClickAsync();
            await SubmitUntil(
                async () =>
                {
                    await Page.GetByLabel("Summary").FillAsync(defectSummary);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
                },
                Page.GetByRole(AriaRole.Heading, new() { Name = defectSummary }));

            await UploadOnCurrentPageAsync(filePath);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public async Task File_list_pages_five_at_a_time_once_there_are_more_than_five()
    {
        await CreateProjectAsync($"E2E pager {Guid.NewGuid():N}");

        if (await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("Attachment storage is not configured on the target instance.");
        }

        var paths = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            var p = Path.Combine(Path.GetTempPath(), $"pager-{i:D2}-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(p, $"content {i}");
            paths.Add(p);
        }

        try
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Add attachments" }).ClickAsync();
            await Page.Locator("dialog[open] input[type=file]").SetInputFilesAsync(paths.ToArray());
            await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();

            var fileRows = Page.Locator(".card").Filter(new() { HasText = "Attachments" }).Locator("ul > li");
            await Expect(Page.GetByText("Page 1 of 2 · 7 files")).ToBeVisibleAsync(new() { Timeout = 15000 });
            await Expect(fileRows).ToHaveCountAsync(5);

            await Page.GetByRole(AriaRole.Link, new() { Name = "Next" }).ClickAsync();
            await Expect(Page.GetByText("Page 2 of 2 · 7 files")).ToBeVisibleAsync();
            await Expect(fileRows).ToHaveCountAsync(2);
        }
        finally
        {
            paths.ForEach(File.Delete);
        }
    }

    [Test]
    public async Task Qa_stages_files_across_picks_removes_one_then_uploads_the_rest()
    {
        var projectName = $"E2E multi-upload {Guid.NewGuid():N}";
        await CreateProjectAsync(projectName);

        if (await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("Attachment storage is not configured on the target instance.");
        }

        var paths = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var p = Path.Combine(Path.GetTempPath(), $"e2e-multi-{i}-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(p, $"file {i} content");
            paths.Add(p);
        }

        try
        {
            var dialog = Page.Locator("dialog[open]");
            var stagedRows = dialog.Locator("[data-upload-list] li");

            await Page.GetByRole(AriaRole.Button, new() { Name = "Add attachments" }).ClickAsync();

            // First pick: two files. Second pick appends rather than replaces.
            await dialog.Locator("input[type=file]").SetInputFilesAsync([paths[0], paths[1]]);
            await Expect(stagedRows).ToHaveCountAsync(2);
            await dialog.Locator("input[type=file]").SetInputFilesAsync(paths[2]);
            await Expect(stagedRows).ToHaveCountAsync(3);

            // Drop the middle file, then upload the remaining two.
            await stagedRows.Filter(new() { HasText = Path.GetFileName(paths[1]) })
                .GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
            await Expect(stagedRows).ToHaveCountAsync(2);

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();

            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = $"Download {Path.GetFileName(paths[0])}" }))
                .ToBeVisibleAsync(new() { Timeout = 15000 });
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = $"Download {Path.GetFileName(paths[2])}" }))
                .ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = $"Download {Path.GetFileName(paths[1])}" }))
                .ToHaveCountAsync(0);
        }
        finally
        {
            paths.ForEach(File.Delete);
        }
    }

    [Test]
    public async Task Downloading_a_file_does_not_navigate_the_page()
    {
        await CreateProjectAsync($"E2E download {Guid.NewGuid():N}");

        if (await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("Attachment storage is not configured on the target instance.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"download-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "the payload");
        var fileName = Path.GetFileName(path);

        try
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Add attachments" }).ClickAsync();
            await Page.Locator("dialog[open] input[type=file]").SetInputFilesAsync(path);
            await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = $"Download {fileName}" }))
                .ToBeVisibleAsync(new() { Timeout = 15000 });

            var urlBefore = Page.Url;

            var download = await Page.RunAndWaitForDownloadAsync(async () =>
                await Page.GetByRole(AriaRole.Link, new() { Name = $"Download {fileName}" }).ClickAsync());

            Assert.That(download.SuggestedFilename, Is.EqualTo(fileName));
            Assert.That(Page.Url, Is.EqualTo(urlBefore), "download must not change the page URL");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Deleting_a_file_needs_confirmation()
    {
        await CreateProjectAsync($"E2E delete {Guid.NewGuid():N}");

        if (await Page.GetByText("Attachment storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("Attachment storage is not configured on the target instance.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"delete-me-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "throwaway");
        var fileName = Path.GetFileName(path);

        try
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Add attachments" }).ClickAsync();
            await Page.Locator("dialog[open] input[type=file]").SetInputFilesAsync(path);
            await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();

            var fileEntry = Page.GetByRole(AriaRole.Link, new() { Name = $"Download {fileName}" });
            await Expect(fileEntry).ToBeVisibleAsync(new() { Timeout = 15000 });

            var row = Page.Locator(".card").Filter(new() { HasText = "Attachments" })
                .Locator("ul > li").Filter(new() { HasText = fileName });

            // Opening the confirmation and cancelling leaves the file in place.
            await row.GetByRole(AriaRole.Button, new() { Name = $"Delete {fileName}" }).ClickAsync();
            await row.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
            await Expect(fileEntry).ToBeVisibleAsync();

            // Confirming removes it.
            await row.GetByRole(AriaRole.Button, new() { Name = $"Delete {fileName}" }).ClickAsync();
            await row.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = $"Download {fileName}" })).ToHaveCountAsync(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Uploads <paramref name="filePath"/> via the AttachmentPanel "Add attachments"
    /// modal on the current page and waits for it to appear in the list.</summary>
    private async Task UploadOnCurrentPageAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        await RetryUntil(
            async () =>
            {
                try
                {
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Add attachments" })
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
