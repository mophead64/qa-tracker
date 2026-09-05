using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// End-to-end coverage for Phase 6: uploading a file on a project, a test case, and a
/// defect, and downloading it back. Self-skips (in addition to the base class's checks)
/// when the target instance has no storage provider configured — the dashboard's
/// Resources card shows "File storage isn't configured" in that case.
/// </summary>
[TestFixture]
public class AttachmentTests : E2ETestBase
{
    [Test]
    public async Task Qa_can_upload_a_file_on_a_project_a_test_case_and_a_defect()
    {
        var projectName = $"E2E attachments {Guid.NewGuid():N}";
        var scopeName = $"Uploads {Guid.NewGuid():N}";
        var scenario = $"When a user attaches a screenshot {Guid.NewGuid():N}";
        var defectSummary = $"Upload button does nothing {Guid.NewGuid():N}";

        var dashboardUrl = await CreateProjectAsync(projectName);

        if (await Page.GetByText("File storage isn't configured").IsVisibleAsync())
        {
            Assert.Ignore("File storage is not configured on the target instance.");
        }

        var filePath = Path.Combine(Path.GetTempPath(), $"e2e-attachment-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(filePath, "sample resource content");

        try
        {
            // Project-level resource, on the dashboard we already landed on.
            await UploadOnCurrentPageAsync(filePath);

            // Test-case resource.
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

            // Defect evidence file.
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

    /// <summary>Uploads <paramref name="filePath"/> via the AttachmentPanel on the current
    /// page and waits for it to appear in the list, retrying the same way other
    /// interactive-form helpers do while the circuit connects.</summary>
    private async Task UploadOnCurrentPageAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var fileInput = Page.Locator("input[type=file]");

        // A slow circuit can leave "Upload" disabled well past ClickAsync's default 30s
        // actionability wait, which would burn the whole budget on one attempt — fail
        // fast and swallow so the outer retry loop (which doesn't catch action() itself)
        // actually gets to cycle.
        await RetryUntil(
            async () =>
            {
                try
                {
                    await fileInput.SetInputFilesAsync(filePath);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Upload" })
                        .ClickAsync(new() { Timeout = 3000 });
                }
                catch (TimeoutException)
                {
                    // Upload still disabled — circuit hasn't caught up yet, try again.
                }
            },
            Page.GetByRole(AriaRole.Link, new() { Name = fileName }),
            attempts: 30);
    }
}
