using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for scope panels on the test cases view, the
/// test cases within them, setting results, and commenting.</summary>
[TestFixture]
public class TestCaseTests : E2ETestBase
{
    [Test]
    public async Task Qa_can_add_a_scope_a_test_case_set_results_and_comment()
    {
        var projectName = $"E2E tc project {Guid.NewGuid():N}";
        var scopeName = $"Authentication {Guid.NewGuid():N}";
        var scenario = $"When a user enters a correct email and password {Guid.NewGuid():N}";
        var note = $"Broken on Safari — modal never opens {Guid.NewGuid():N}";

        var dashboardUrl = await CreateProjectAsync(projectName);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Test cases" }).First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases$"));

        // Create a scope — lands back on the test cases view as a panel.
        await Page.GetByRole(AriaRole.Link, new() { Name = "New scope" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/scopes/new$"));
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Name").FillAsync(scopeName);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create scope" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = scopeName }));
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases$"));

        // Add a test case from the panel.
        await Page.GetByRole(AriaRole.Link, new() { Name = "New test case" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/cases/new$"));
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Scenario").FillAsync(scenario);
                await Page.GetByLabel("Steps").FillAsync("1. Open login\n2. Enter details\n3. Submit");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create test case" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = scenario }));
        await Expect(Page.Locator("summary[aria-label='Change result']")).ToContainTextAsync("Not run");

        // The panel entry has a quick action to mark it passed.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Test cases" }).First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = scopeName })).ToBeVisibleAsync();
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Pass", Exact = true }).First.ClickAsync(),
            Page.GetByText("Passed").First);

        // The test case page can also set the result, and takes comments.
        await Page.GetByRole(AriaRole.Link, new() { Name = scenario }).First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/cases/[0-9a-fA-F-]{36}$"));

        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change result']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Failed", Exact = true }).ClickAsync();
            },
            Page.GetByText("Failed", new() { Exact = true }).First);

        await RetryUntil(
            async () =>
            {
                await Page.GetByLabel("Add a comment").FillAsync(note);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            },
            Page.GetByText(note));

        await Page.ReloadAsync();
        await Expect(Page.GetByText(note)).ToBeVisibleAsync();
        await Expect(Page.GetByText("Failed", new() { Exact = true }).First).ToBeVisibleAsync();

        // Dashboard reflects the final result.
        await Page.GotoAsync(dashboardUrl);
        await Expect(Page.GetByText("0 passed · 1 failed · 0 not run")).ToBeVisibleAsync();

        // CSV export is offered.
        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Export CSV" })).ToBeVisibleAsync();
    }
}
