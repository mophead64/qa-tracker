using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>Defect templates (system default → project default → per defect) and severity on create.</summary>
[TestFixture]
public class DefectTemplateTests : E2ETestBase
{
    private ILocator Radio(string value) => Page.Locator($"input[data-template-option='{value}']");

    /// <summary>Clicks the visible switch segment (the radio itself is visually hidden).</summary>
    private Task PickAsync(string value) =>
        Page.Locator("label.segmented-option", new() { Has = Radio(value) }).ClickAsync();

    [Test]
    public async Task Project_template_drives_the_form_and_a_defect_can_switch()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var summary = $"Simple defect {suffix}";
        var dashboardUrl = await CreateProjectAsync($"E2E templates {suffix}");

        // Project default: Simple (the system default stays Advanced).
        await Page.GotoAsync($"{dashboardUrl}/edit");
        await Expect(Radio("inherit")).ToBeCheckedAsync();
        await PickAsync("Simple");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(dashboardUrl);

        // The new-defect form starts on Simple: one "Description" box, no expected / actual.
        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await Expect(Radio("Simple")).ToBeCheckedAsync();
        await Expect(Page.GetByLabel("Description")).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Expected results")).ToBeHiddenAsync();
        await Expect(Page.GetByLabel("Actual results")).ToBeHiddenAsync();

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "defect-form-simple.png"), FullPage = true });
        }

        // Picking Advanced shows them straight away (no reload), and back again.
        await PickAsync("Advanced");
        await Expect(Page.GetByLabel("Expected results")).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Repro steps")).ToBeVisibleAsync();
        await PickAsync("Simple");
        await Expect(Page.GetByLabel("Expected results")).ToBeHiddenAsync();

        // Severity is set on create.
        await Page.GetByLabel("Summary").FillAsync(summary);
        await Page.GetByLabel("Description").FillAsync("The save button does nothing");
        // Colour-coded like the defect page's severity menu: Medium (blue) → High (amber).
        var severity = Page.Locator("details[data-form-select] > summary[aria-label='Severity']");
        await Expect(severity).ToHaveClassAsync(new Regex(@"\bchip-blue\b"));
        await PickFormSelectAsync("Severity", "High");
        await Expect(severity).ToHaveClassAsync(new Regex(@"\bchip-amber\b"));
        await Expect(severity).Not.ToHaveClassAsync(new Regex(@"\bchip-blue\b"));

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } shots)
        {
            await severity.ClickAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(shots, "defect-form-severity.png") });
            await severity.ClickAsync();
        }
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = summary })).ToBeVisibleAsync();
        var defectUrl = Page.Url;

        await Expect(Page.Locator("summary[aria-label='Change severity']")).ToContainTextAsync("High");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Description", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("The save button does nothing")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Expected results" })).ToHaveCountAsync(0);

        // Switch this defect to Advanced on edit; the description carries over as repro steps.
        await Page.GotoAsync($"{defectUrl}/edit");
        await Expect(Radio("Simple")).ToBeCheckedAsync();
        await PickAsync("Advanced");
        await Expect(Page.GetByLabel("Repro steps")).ToHaveValueAsync("The save button does nothing");
        await Page.GetByLabel("Expected results").FillAsync("It saves");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(defectUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Repro steps" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("It saves", new() { Exact = true })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#history-modal [data-history-kind='TemplateChanged']"))
            .ToContainTextAsync("switched it to the Advanced template");
    }

    [Test]
    public async Task System_default_applies_to_projects_without_their_own()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E system template {Guid.NewGuid():N}");

        try
        {
            await SetSystemTemplateAsync("Simple");

            await Page.GotoAsync($"{dashboardUrl}/defects/new");
            await Expect(Radio("Simple")).ToBeCheckedAsync();

            // The project form shows what "System default" currently means.
            await Page.GotoAsync($"{dashboardUrl}/edit");
            await Expect(Radio("inherit")).ToBeCheckedAsync();
            await Expect(Page.Locator("label", new() { Has = Radio("inherit") })).ToHaveTextAsync("System default (Simple)");
        }
        finally
        {
            // Global setting: always put it back so other tests see the usual Advanced form.
            await SetSystemTemplateAsync("Advanced");
        }

        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await Expect(Radio("Advanced")).ToBeCheckedAsync();
    }

    private async Task SetSystemTemplateAsync(string value)
    {
        await Page.GotoAsync($"{BaseUrl}/admin");
        // Applies on click, like the notification switch — no Save button.
        var card = Page.Locator("#defect-template");
        await card.Locator($"button[data-template-option='{value}']").ClickAsync();
        await Expect(Page.GetByText("Default defect template saved.")).ToBeVisibleAsync();
        await Expect(card.Locator($"button[data-template-option='{value}']")).ToHaveAttributeAsync("aria-pressed", "true");
    }
}
