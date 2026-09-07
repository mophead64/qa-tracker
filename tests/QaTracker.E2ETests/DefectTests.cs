using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for raising a defect, moving it through the fix
/// workflow, commenting, and creating a test case from it.</summary>
[TestFixture]
public class DefectTests : E2ETestBase
{
    [Test]
    public async Task Qa_can_raise_a_defect_track_it_and_spin_up_a_test_case()
    {
        var projectName = $"E2E defect project {Guid.NewGuid():N}";
        var summary = $"Save button does nothing on Safari {Guid.NewGuid():N}";
        var scopeName = $"Checkout {Guid.NewGuid():N}";
        var note = $"Repro'd on 17.4 — no network call fires {Guid.NewGuid():N}";

        var dashboardUrl = await CreateProjectAsync(projectName);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Defects" }).First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/defects$"));

        await Page.GetByRole(AriaRole.Link, new() { Name = "New defect" }).First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/defects/new$"));

        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByLabel("Repro steps").FillAsync("1. Open checkout\n2. Click Save");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));

        await Expect(Page).ToHaveURLAsync(new Regex("/defects/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByText("D-1")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Not fixed").First).ToBeVisibleAsync();

        // Move it along the workflow.
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Fixing" }).ClickAsync();
            },
            Page.Locator("span", new() { HasTextString = "Fixing" }).First);

        // Comment, and make sure it survives a reload.
        await RetryUntil(
            async () =>
            {
                await Page.GetByLabel("Add a comment").FillAsync(note);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            },
            Page.GetByText(note));
        await Page.ReloadAsync();
        await Expect(Page.GetByText(note)).ToBeVisibleAsync();

        // From the "Link test cases" modal, jump to the test-case pages to create one.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Link test cases" }).ClickAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Link, new() { Name = "Create a new test case" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/test-cases\?fromDefect="));

        // Add a scope, then the test case — which lands prefilled from the defect.
        await Page.GetByRole(AriaRole.Link, new() { Name = "New scope" }).ClickAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Name").FillAsync(scopeName);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create scope" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = "New test case" }));

        await Expect(Page.GetByLabel("Scenario")).ToHaveValueAsync(summary);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create test case" }).ClickAsync();

        // Back on the defect, with the new case linked.
        await Expect(Page).ToHaveURLAsync(new Regex("/defects/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Linked test cases" })).ToBeVisibleAsync();

        // The linked case shows the defect back.
        await Page.GetByRole(AriaRole.Link, new() { Name = scopeName }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/cases/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByText("Linked defects")).ToBeVisibleAsync();
        await Expect(Page.GetByText("D-1")).ToBeVisibleAsync();

        // Dashboard counts it as open.
        await Page.GotoAsync(dashboardUrl);
        await Expect(Page.GetByText("Open defects")).ToBeVisibleAsync();
        await Expect(Page.Locator("a", new() { HasTextString = "Open defects" })).ToContainTextAsync("1");
    }

    [Test]
    public async Task Defect_list_groups_by_assignee_and_status()
    {
        var projectName = $"E2E defect groups {Guid.NewGuid():N}";
        var summary = $"Totals row shows stale count {Guid.NewGuid():N}";

        var dashboardUrl = await CreateProjectAsync(projectName);
        await AddSelfToTeamAsync(dashboardUrl);
        await Page.GotoAsync(dashboardUrl);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Defects" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "New defect" }).First.ClickAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));
        var defectUrl = Page.Url;

        // New + unassigned -> "Unassigned" group.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Defects" }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Unassigned" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "To verify" })).Not.ToBeVisibleAsync();

        // Assign to self via the edit form; retry until the detail page shows it stuck.
        var me = await CurrentUserNameAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GotoAsync($"{defectUrl}/edit");
                await Page.GetByLabel("Assigned to").SelectOptionAsync(new SelectOptionValue { Label = $"{me} (QA)" });
                await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change assignee']").Filter(new() { HasTextString = me }));

        // Still "Not fixed" + assigned to me -> "To fix" queue.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Defects" }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "To fix" })).ToBeVisibleAsync();

        // Mark "To check" -> moves to the "To verify" queue.
        await Page.GotoAsync(defectUrl);
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "To check" }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change status']").Filter(new() { HasTextString = "To check" }));

        await Page.GetByRole(AriaRole.Link, new() { Name = "Defects" }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "To verify" })).ToBeVisibleAsync();
        await Expect(Page.GetByText(summary)).ToBeVisibleAsync();
    }

    [Test]
    public async Task Fix_workflow_dev_marks_fixed_qa_rejects_then_verifies()
    {
        var devEmail = $"e2e-fix-dev-{Guid.NewGuid():N}@test.local";
        const string devPassword = "Str0ng!Passw0rd";
        var projectName = $"E2E fix workflow {Guid.NewGuid():N}";
        var summary = $"Checkout total is off by a cent {Guid.NewGuid():N}";

        await CreateUserAsync(devEmail, "E2E Fix Dev", "Dev", devPassword);

        var dashboardUrl = await CreateProjectAsync(projectName);
        var qaName = await CurrentUserNameAsync();
        await AddSelfToTeamAsync(dashboardUrl); // a QA on the team for "Mark as fixed" to hand off to

        // Raise a defect as QA.
        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));
        var defectUrl = Page.Url;

        // Sign in as the Dev in a separate context and run the fix workflow.
        await using var devContext = await Browser.NewContextAsync();
        var devPage = await devContext.NewPageAsync();
        await devPage.GotoAsync($"{BaseUrl}/Account/Login");
        await devPage.GetByLabel("Email").FillAsync(devEmail);
        await devPage.GetByLabel("Password").FillAsync(devPassword);
        await devPage.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(devPage).Not.ToHaveURLAsync(new Regex("/Account/Login"));

        await devPage.GotoAsync(defectUrl);
        await RetryUntil(
            () => devPage.GetByRole(AriaRole.Button, new() { Name = "Start fixing" }).ClickAsync(),
            devPage.GetByText("Fixing").First);
        // "E2E Fix Dev" also appears in the dev's own top-bar user menu, so scope to the page body.
        await Expect(devPage.GetByRole(AriaRole.Main).GetByText("E2E Fix Dev")).ToBeVisibleAsync(); // now assigned to the dev

        await RetryUntil(
            () => devPage.GetByRole(AriaRole.Button, new() { Name = "Mark as fixed" }).ClickAsync(),
            devPage.GetByText("To check").First);
        await Expect(devPage.GetByText("fixed by E2E Fix Dev")).ToBeVisibleAsync();

        // QA rejects the fix -> it goes back to the dev.
        await Page.GotoAsync(defectUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Not fixed", Exact = true }).ClickAsync(),
            Page.Locator("summary[aria-label='Change assignee']").Filter(new() { HasTextString = "E2E Fix Dev" }));

        // Dev fixes it again, QA verifies -> Fixed, "tested by <QA>", unassigned.
        await devPage.GotoAsync(defectUrl);
        await RetryUntil(
            () => devPage.GetByRole(AriaRole.Button, new() { Name = "Start fixing" }).ClickAsync(),
            devPage.GetByText("Fixing").First);
        await RetryUntil(
            () => devPage.GetByRole(AriaRole.Button, new() { Name = "Mark as fixed" }).ClickAsync(),
            devPage.GetByText("To check").First);

        await Page.GotoAsync(defectUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Fixed", Exact = true }).ClickAsync(),
            Page.GetByText($"tested by {qaName}"));
        await Expect(Page.Locator("summary[aria-label='Change assignee']")
            .Filter(new() { HasTextString = "Unassigned" })).ToBeVisibleAsync();

        // (The throwaway Dev is left in place — it's now recorded as this defect's "fixed by",
        //  a Restrict FK, so it can't be deleted without deleting the defect first.)
    }
}
