using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// End-to-end coverage for the CSV test-case importer: the always-visible entry point, the
/// validated preview (warnings + blocking errors), committing the import, and re-importing
/// over existing data.
/// </summary>
[TestFixture]
public class TestCaseImportTests : E2ETestBase
{
    [Test]
    public async Task Qa_can_validate_and_import_a_csv_then_re_import_over_it()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var scopeName = $"Auth {suffix}";
        var scenario = $"Account locks after 3 failed logins {suffix}";
        var defectSummary = $"Lockout never triggers {suffix}";

        var dashboardUrl = await CreateProjectAsync($"E2E import project {suffix}");

        // A defect to link to (the project's first defect, so it's D-1).
        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(defectSummary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = defectSummary }));

        // Import is offered on the test-cases view even with nothing there yet.
        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Import CSV" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Import CSV" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases/import$"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Download the template" })).ToBeVisibleAsync();

        var csv =
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
            $"{scopeName},Functional,{scenario},Open the app,D-1\n" +
            ",,,Enter the wrong password three times,\n" +
            ",,,Expect the account to be locked,\n" +
            $"{scopeName},Functional,Login page loads within 1s {suffix},load the page,D-99\n";

        await ValidateCsvAsync(csv);

        await Expect(Page.GetByText(scenario).First).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex("D-99.*doesn't exist")).First).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex("D-1.*will be linked to")).First).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/test-cases\?imported="));
        await Expect(Page.GetByText(new Regex("Imported .*scope")).First).ToBeVisibleAsync();

        // The scope, case and its multi-line steps landed, with the defect linked.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = scopeName })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = scenario }).ClickAsync();
        await Expect(Page.GetByText("Enter the wrong password three times")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Linked defects" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("D-1", new() { Exact = true })).ToBeVisibleAsync();

        // Re-importing the same file now updates rather than creates.
        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await ValidateCsvAsync(csv);
        await Expect(Page.GetByText(new Regex("steps will be overwritten")).First).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex("D-1 is already linked")).First).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false }).ClickAsync();
        await Expect(Page.GetByText(new Regex("Imported .*updated test case")).First).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_non_csv_upload_is_rejected()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E import reject {Guid.NewGuid():N}");

        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await SubmitUntil(
            async () =>
            {
                await Page.SetInputFilesAsync("input[type=file]", new FilePayload
                {
                    Name = "notes.txt",
                    MimeType = "text/plain",
                    Buffer = System.Text.Encoding.UTF8.GetBytes("not a csv"),
                });
                await Page.GetByRole(AriaRole.Button, new() { Name = "Validate" }).ClickAsync();
            },
            Page.GetByText(new Regex("Only .csv files")));

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false })).Not.ToBeVisibleAsync();
    }

    private async Task ValidateCsvAsync(string csv)
    {
        await SubmitUntil(
            async () =>
            {
                await Page.SetInputFilesAsync("input[type=file]", new FilePayload
                {
                    Name = "import.csv",
                    MimeType = "text/csv",
                    Buffer = System.Text.Encoding.UTF8.GetBytes(csv),
                });
                await Page.GetByRole(AriaRole.Button, new() { Name = "Validate" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false }));
    }
}
