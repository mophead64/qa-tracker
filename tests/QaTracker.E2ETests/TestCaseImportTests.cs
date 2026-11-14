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

        await Expect(Page.Locator($"[data-import-case='{scenario}']")).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex("D-99.*doesn't exist")).First).ToBeVisibleAsync();
        // The neutral "what will happen" notes are folded away; open them.
        await Page.Locator("[data-import-infos] summary").ClickAsync();
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

        // Re-importing the same file: it's compared against what's there, so nothing to do.
        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await ValidateCsvAsync(csv);
        await Expect(Page.Locator("[data-import-nothing]")).ToContainTextAsync("Nothing to change");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Validate_is_hidden_until_a_file_is_chosen_on_a_full_page_load()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E import first load {Guid.NewGuid():N}");

        // A full load straight to the page (not an enhanced navigation from the list) — the
        // button must never show before a file is chosen, not even briefly.
        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Validate" })).ToBeHiddenAsync();

        // Hidden by CSS, not by a script that runs later (which let it show until then): with
        // JavaScript off it's still hidden, and appears once a file is chosen.
        await using var noJs = await Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        var page = await noJs.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/Account/Login");
        await page.GetByLabel("Email").FillAsync(Environment.GetEnvironmentVariable("QATRACKER_E2E_EMAIL")!);
        await page.GetByLabel("Password").FillAsync(Environment.GetEnvironmentVariable("QATRACKER_E2E_PASSWORD")!);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Validate" })).ToBeHiddenAsync();
        await page.SetInputFilesAsync("input[type=file]", new FilePayload
        {
            Name = "plan.csv",
            MimeType = "text/csv",
            Buffer = System.Text.Encoding.UTF8.GetBytes("Test Case Scope,Test Case Type,Test Case Scenario\n"),
        });
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Validate" })).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Validate" })).ToBeHiddenAsync();
    }

    [Test]
    public async Task Split_buttons_offer_the_template_and_an_export_with_results()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var dashboardUrl = await CreateProjectAsync($"E2E split buttons {suffix}");
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Auth {suffix}", $"Sign in works {suffix}"))[0];
        await Page.GotoAsync(caseUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Mark as passed" }).ClickAsync(),
            Page.Locator("[data-tested-by]"));
        var me = await CurrentUserNameAsync();

        await Page.GotoAsync($"{dashboardUrl}/test-cases");

        // Export ▾ → Export with results: the importer's columns plus result / tester / date.
        await Page.GetByRole(AriaRole.Button, new() { Name = "More export options" }).ClickAsync();
        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "split-export.png") });
        }
        var withResults = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Link, new() { Name = "Export with results" }).ClickAsync());
        var exported = await System.IO.File.ReadAllTextAsync(await withResults.PathAsync());
        Assert.That(exported, Does.StartWith("Scope ID,Test Case Scope,Test Case Type,Test Case ID,Test Case Scenario,Steps,DefectLink,Result,Tested By,Tested (UTC)"));
        Assert.That(exported, Does.Contain($"Sign in works {suffix},,,Passed,{me},"));

        // Import ▾ → Download template.
        await Page.GetByRole(AriaRole.Button, new() { Name = "More import options" }).ClickAsync();
        var template = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Link, new() { Name = "Download template" }).ClickAsync());
        Assert.That(template.SuggestedFilename, Is.EqualTo("test-case-import-template.csv"));
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases$")); // a download, not a navigation

        // The importer's Validate button only appears once a file is chosen.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Import CSV", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Validate" })).ToBeHiddenAsync();
        await Page.SetInputFilesAsync("input[type=file]", new FilePayload
        {
            Name = "plan.csv",
            MimeType = "text/csv",
            Buffer = System.Text.Encoding.UTF8.GetBytes("Test Case Scope,Test Case Type,Test Case Scenario\n"),
        });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Validate" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Validate" })).ToBeHiddenAsync();
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

    [Test]
    public async Task Exported_csv_can_be_bulk_edited_and_re_imported()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var dashboardUrl = await CreateProjectAsync($"E2E export round-trip {suffix}");

        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await ValidateCsvAsync(
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
            $"Auth {suffix},Functional,Sign in works {suffix},open the app,\n" +
            ",,,enter credentials,\n");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false }).ClickAsync();
        await Expect(Page.GetByText(new Regex("Imported .*scope")).First).ToBeVisibleAsync();

        // Export the plan, add a row in a spreadsheet, and re-import.
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Link, new() { Name = "Export CSV" }).ClickAsync());
        var exported = await System.IO.File.ReadAllTextAsync(await download.PathAsync());

        Assert.That(exported, Does.StartWith("Scope ID,Test Case Scope,Test Case Type,Test Case ID,Test Case Scenario,Steps,DefectLink"));
        Assert.That(exported, Does.Match($@"S-\d+,Auth {suffix},Functional,T-\d+,Sign in works {suffix},open the app,"));

        // Re-importing the untouched export changes nothing, so it's all hidden.
        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await ValidateCsvAsync(exported);
        await Expect(Page.Locator("[data-import-nothing]")).ToBeVisibleAsync();

        // Now edit one step and add a scope: only those show, the step as a diff.
        var edited = exported.Replace(",enter credentials,", ",enter email and password,").TrimEnd('\n')
            + $"\n,Billing {suffix},Functional,,Invoices are generated {suffix},create an order,\n";
        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await ValidateCsvAsync(edited);
        await Expect(Page.GetByText("Existing scope").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("New scope").First).ToBeVisibleAsync();
        var updated = Page.Locator($"[data-import-case='Sign in works {suffix}']");
        await Expect(updated.Locator("[data-diff='Removed']")).ToHaveTextAsync(new Regex("enter credentials"));
        await Expect(updated.Locator("[data-diff='Added']")).ToHaveTextAsync(new Regex("enter email and password"));
        await Expect(updated.Locator("[data-diff='Same']")).ToHaveTextAsync(new Regex("open the app"));

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await updated.ScrollIntoViewIfNeededAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "import-diff.png") });
        }

        await Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false }).ClickAsync();
        await Expect(Page.GetByText(new Regex("Imported 1 new scope.*1 updated test case")).First).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Billing {suffix}" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Ids_in_the_file_rename_and_move_and_problems_are_shown_on_their_rows()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E import ids {suffix}");
        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await ValidateCsvAsync(
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
            "Auth,Functional,Login,open,\n" +
            "Account,Functional,Profile,open,\n");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false }).ClickAsync();
        await Expect(Page.GetByText(new Regex("Imported .*scope")).First).ToBeVisibleAsync();

        // The plan shows the new IDs: S-1 Auth / T-1 Login, S-2 Account / T-2 Profile.
        await Expect(Page.Locator("[data-scope-ref]").First).ToHaveTextAsync("S-1");
        await Expect(Page.Locator("[data-case-ref]").First).ToHaveTextAsync("T-1");

        // By ID: rename S-1, move T-2 into it, plus a new scope with no type and a name clash.
        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await ValidateCsvAsync(
            "Scope ID,Test Case Scope,Test Case Type,Test Case ID,Test Case Scenario,Steps,DefectLink\n" +
            "S-1,Authentication,Functional,T-1,Login,open,\n" +
            ",,,T-2,Profile,open,\n" +
            ",Extras,,,Login,check,\n");

        var renamed = Page.Locator("[data-import-scope='Authentication']");
        await Expect(renamed.Locator("[data-old-name]")).ToHaveTextAsync("Auth");
        await Expect(renamed.GetByText("Renamed", new() { Exact = true })).ToBeVisibleAsync();
        var moved = renamed.Locator("[data-import-case='Profile']");
        await Expect(moved.Locator("[data-moved-from]")).ToContainTextAsync("Account");
        await Expect(moved.Locator("[data-case-ref]")).ToHaveTextAsync("T-2");

        var extras = Page.Locator("[data-import-scope='Extras']");
        await Expect(extras.Locator("[data-kind-assumed]")).ToBeVisibleAsync();
        await Expect(extras.Locator("[data-case-issue='Warning']").Filter(new() { HasTextString = "is also in \"Authentication\"" })).ToHaveCountAsync(1);
        // The other "Login" (T-1) is in the file too, so there's no second warning about it.
        await Expect(extras.Locator("[data-case-issue='Warning']").Filter(new() { HasTextString = "already exists in scope" }))
            .ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-import-warnings] [data-issue-row]").First).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-import-status]")).ToHaveTextAsync("Ready to import");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false }).ClickAsync();
        await Expect(Page.GetByText(new Regex("1 renamed scope.*1 moved test case")).First).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Authentication" })).ToBeVisibleAsync();

        // A duplicate name inside one scope blocks the import, with the rows named.
        await Page.GotoAsync($"{dashboardUrl}/test-cases/import");
        await SubmitUntil(
            async () =>
            {
                await Page.SetInputFilesAsync("input[type=file]", new FilePayload
                {
                    Name = "dup.csv", MimeType = "text/csv",
                    Buffer = System.Text.Encoding.UTF8.GetBytes(
                        "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
                        "Billing,Functional,Invoice,a,\nBilling,Functional,Refund,b,\nBilling,Functional,Invoice,c,\n"),
                });
                await Page.GetByRole(AriaRole.Button, new() { Name = "Validate" }).ClickAsync();
            },
            Page.Locator("[data-import-errors]"));
        await Expect(Page.Locator("[data-import-errors]")).ToContainTextAsync("more than one test case called \"Invoice\" (rows 2, 4)");
        await Expect(Page.Locator("[data-import-status]")).ToHaveTextAsync("1 error to fix");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false })).ToHaveCountAsync(0);
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
            // The preview has rendered: either something to import, or "Nothing to change".
            Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = false })
                .Or(Page.Locator("[data-import-nothing]")));
    }
}
