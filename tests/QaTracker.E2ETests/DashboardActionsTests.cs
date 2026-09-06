using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for the project dashboard's "Actions needed"
/// section — a failed test case with no defect, then raising and self-assigning one.</summary>
[TestFixture]
public class DashboardActionsTests : E2ETestBase
{
    [Test]
    public async Task Dashboard_flags_a_failed_test_then_tracks_its_defect()
    {
        var projectName = $"E2E dashboard actions {Guid.NewGuid():N}";
        var scopeName = $"Checkout {Guid.NewGuid():N}";
        var scenario = $"When payment fails the order is not created {Guid.NewGuid():N}";

        var dashboardUrl = await CreateProjectAsync(projectName);

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

        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Mark failed" }).ClickAsync(),
            Page.GetByText("Failed", new() { Exact = true }).First);
        var caseUrl = Page.Url;

        // No defect raised yet -> flagged on the dashboard.
        await Page.GotoAsync(dashboardUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Test case follow-ups" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Failed, no defect raised")).ToBeVisibleAsync();
        await Expect(Page.GetByText(scenario)).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Defects to verify" })).Not.ToBeVisibleAsync();

        // Raise a defect for it, straight from the test case.
        await Page.GotoAsync(caseUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Create new defect" }).ClickAsync();
        await SubmitUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync(),
            Page.GetByRole(AriaRole.Heading, new() { Name = scenario }));
        var defectUrl = Page.Url;

        // Assign it to the signed-in QA and mark it "To check".
        var me = Environment.GetEnvironmentVariable("QATRACKER_E2E_DISPLAYNAME") ?? "E2E QA Bot";
        await SubmitUntil(
            async () =>
            {
                await Page.GotoAsync($"{defectUrl}/edit");
                await Page.GetByLabel("Assigned to").SelectOptionAsync(new SelectOptionValue { Label = $"[QA] {me}" });
                await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change assignee']").Filter(new() { HasTextString = me }));
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "To check" }).ClickAsync();
            },
            Page.Locator("span.badge").Filter(new() { HasTextString = "To check" }));

        // The dashboard now shows it as a personal "to verify" item, and the test case
        // drops out of the follow-ups list (it has an open defect, but isn't unaccounted for).
        await Page.GotoAsync(dashboardUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Defects to verify" })).ToBeVisibleAsync();
        await Expect(Page.GetByText(scenario)).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Test case follow-ups" })).Not.ToBeVisibleAsync();
    }
}
