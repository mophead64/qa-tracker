using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// The /admin "Developers can manage projects / test cases / defects" toggles, seen from a
/// Dev's side: each area's create/edit controls (and direct URLs) follow its own toggle.
/// The toggles are instance-wide, so this fixture never runs alongside others and puts them
/// back the way it found them.
/// </summary>
[TestFixture]
[NonParallelizable]
public class DeveloperPermissionsTests : E2ETestBase
{
    private const string ProjectsLabel = "Developers can manage projects";
    private const string TestCasesLabel = "Developers can manage test cases";
    private const string DefectsLabel = "Developers can manage defects";

    [Test]
    public async Task Developer_controls_follow_the_admin_permission_toggles()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var devName = $"Perm Dev {suffix}";
        var devEmail = $"e2e-perm-{suffix}@test.local";
        var scenario = $"Perm scenario {suffix}";
        var summary = $"Perm defect {suffix}";

        await CreateUserAsync(devEmail, devName, "Dev", DevPassword);
        var dashboardUrl = await CreateProjectAsync($"E2E perm project {suffix}");
        await AddToTeamAsync(dashboardUrl, devName);

        // Something to look at in each area.
        await Page.GotoAsync($"{dashboardUrl}/test-cases/scopes/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Name").FillAsync($"Scope {suffix}");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create scope" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = $"Scope {suffix}" }));
        await Page.GetByRole(AriaRole.Link, new() { Name = "New test case" }).First.ClickAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Scenario").FillAsync(scenario);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create test case" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = scenario }));
        var testCaseUrl = Page.Url;

        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));
        var defectUrl = Page.Url;

        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);

        var original = await ReadTogglesAsync();
        try
        {
            // Everything off: a Dev can look but not create or change anything.
            await SetTogglesAsync(projects: false, testCases: false, defects: false);
            await AssertProjectsAsync(devPage, dashboardUrl, canManage: false);
            await AssertTestCasesAsync(devPage, dashboardUrl, testCaseUrl, canManage: false);
            await AssertDefectsAsync(devPage, dashboardUrl, defectUrl, canManage: false);

            // Only defects on: the other two areas stay read-only.
            await SetTogglesAsync(projects: false, testCases: false, defects: true);
            await AssertDefectsAsync(devPage, dashboardUrl, defectUrl, canManage: true);
            await AssertTestCasesAsync(devPage, dashboardUrl, testCaseUrl, canManage: false);
            await AssertProjectsAsync(devPage, dashboardUrl, canManage: false);

            // Everything on: the controls are back.
            await SetTogglesAsync(projects: true, testCases: true, defects: true);
            await AssertProjectsAsync(devPage, dashboardUrl, canManage: true);
            await AssertTestCasesAsync(devPage, dashboardUrl, testCaseUrl, canManage: true);
            await AssertDefectsAsync(devPage, dashboardUrl, defectUrl, canManage: true);
        }
        finally
        {
            await SetTogglesAsync(original.Projects, original.TestCases, original.Defects);
        }

        await DeleteUserAsync(devEmail);
    }

    private async Task AssertProjectsAsync(IPage dev, string dashboardUrl, bool canManage)
    {
        await dev.GotoAsync(dashboardUrl);
        await Expect(dev.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }))
            .ToHaveCountAsync(canManage ? 1 : 0);

        // Going straight to the edit form is refused too.
        await dev.GotoAsync($"{dashboardUrl}/edit");
        await Expect(dev.GetByRole(AriaRole.Button, new() { Name = "Save changes" }))
            .ToHaveCountAsync(canManage ? 1 : 0);
    }

    private async Task AssertTestCasesAsync(IPage dev, string dashboardUrl, string testCaseUrl, bool canManage)
    {
        var expected = canManage ? 1 : 0;

        await dev.GotoAsync($"{dashboardUrl}/test-cases");
        await Expect(dev.GetByRole(AriaRole.Link, new() { Name = "New scope" })).ToHaveCountAsync(expected);
        await Expect(dev.GetByRole(AriaRole.Link, new() { Name = "Edit scope" })).ToHaveCountAsync(expected);
        await Expect(dev.GetByRole(AriaRole.Link, new() { Name = "New test case" })).ToHaveCountAsync(expected);

        await dev.GotoAsync(testCaseUrl);
        await Expect(dev.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true })).ToHaveCountAsync(expected);

        await dev.GotoAsync($"{testCaseUrl}/edit");
        await Expect(dev.GetByRole(AriaRole.Button, new() { Name = "Save changes" })).ToHaveCountAsync(expected);
    }

    private async Task AssertDefectsAsync(IPage dev, string dashboardUrl, string defectUrl, bool canManage)
    {
        var expected = canManage ? 1 : 0;

        await dev.GotoAsync($"{dashboardUrl}/defects");
        await Expect(dev.GetByRole(AriaRole.Link, new() { Name = "New defect" })).ToHaveCountAsync(expected);

        await dev.GotoAsync(defectUrl);
        await Expect(dev.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true })).ToHaveCountAsync(expected);

        await dev.GotoAsync($"{dashboardUrl}/defects/new");
        await Expect(dev.GetByRole(AriaRole.Button, new() { Name = "Create defect" })).ToHaveCountAsync(expected);
    }

    private async Task<(bool Projects, bool TestCases, bool Defects)> ReadTogglesAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/admin");
        return (
            await Page.GetByLabel(ProjectsLabel).IsCheckedAsync(),
            await Page.GetByLabel(TestCasesLabel).IsCheckedAsync(),
            await Page.GetByLabel(DefectsLabel).IsCheckedAsync());
    }

    private async Task SetTogglesAsync(bool projects, bool testCases, bool defects)
    {
        await Page.GotoAsync($"{BaseUrl}/admin");
        await Page.GetByLabel(ProjectsLabel).SetCheckedAsync(projects);
        await Page.GetByLabel(TestCasesLabel).SetCheckedAsync(testCases);
        await Page.GetByLabel(DefectsLabel).SetCheckedAsync(defects);
        await Page.Locator("form").Filter(new() { Has = Page.GetByLabel(DefectsLabel) })
            .GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Developer permissions saved.")).ToBeVisibleAsync();
    }
}
