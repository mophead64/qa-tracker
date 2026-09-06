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

        // Create a test case from the defect, into a brand-new scope.
        await Page.Locator("summary[aria-label='Create test case']").ClickAsync();
        await Page.GetByLabel("Scope for the new test case").SelectOptionAsync(new SelectOptionValue { Value = "new" });
        await Page.GetByPlaceholder("New scope name").FillAsync(scopeName);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = scopeName })).ToBeVisibleAsync();

        // The defect now lists the case under "Linked test cases".
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
            Page.Locator("span.badge").Filter(new() { HasTextString = "To check" }));

        await Page.GetByRole(AriaRole.Link, new() { Name = "Defects" }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "To verify" })).ToBeVisibleAsync();
        await Expect(Page.GetByText(summary)).ToBeVisibleAsync();
    }
}
