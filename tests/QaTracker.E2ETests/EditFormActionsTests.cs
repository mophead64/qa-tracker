using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The shared edit-form footer (FormActions), its delete dialog and the Danger zone.</summary>
[TestFixture]
public class EditFormActionsTests : E2ETestBase
{
    private ILocator Footer => Page.Locator("[data-form-actions]");
    private ILocator Save => Footer.Locator("[data-form-save]");
    private ILocator OpenDialog => Page.Locator("dialog[open]");

    [Test]
    public async Task Test_case_footer_save_cancel_delete_with_shortcuts_and_unsaved_changes_warning()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var scenario = $"Footer case {suffix}";
        var dashboardUrl = await CreateProjectAsync($"E2E footer {suffix}");
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Scope {suffix}", scenario))[0];
        await Page.GotoAsync($"{caseUrl}/edit");

        // [Save changes] [Cancel] … [Delete test case], left to right, in the card's footer.
        var save = (await Save.BoundingBoxAsync())!;
        var cancel = (await Footer.Locator("[data-form-cancel]").BoundingBoxAsync())!;
        var delete = (await Footer.GetByRole(AriaRole.Button, new() { Name = "Delete test case" }).BoundingBoxAsync())!;
        Assert.That(save.X, Is.LessThan(cancel.X));
        Assert.That(cancel.X + cancel.Width, Is.LessThan(delete.X - 100), "Delete sits far right, away from Save");
        Assert.That(save.Height, Is.EqualTo(cancel.Height).Within(0.5));
        Assert.That(save.Height, Is.EqualTo(delete.Height).Within(0.5));

        // Save is disabled until something changes — and again if it's changed back.
        await Expect(Save).ToBeDisabledAsync();
        await Page.GetByLabel("Scenario").FillAsync(scenario + " edited");
        await Expect(Save).ToBeEnabledAsync();
        await Page.GetByLabel("Scenario").FillAsync(scenario);
        await Expect(Save).ToBeDisabledAsync();

        // Unsaved changes: Cancel asks first; dismissing stays put.
        await Page.GetByLabel("Steps").FillAsync("1. Open the page");
        string? asked = null;
        void Dismiss(object? _, IDialog d) { asked = d.Message; _ = d.DismissAsync(); }
        Page.Dialog += Dismiss;
        await Footer.Locator("[data-form-cancel]").ClickAsync();
        Page.Dialog -= Dismiss;
        Assert.That(asked, Does.Contain("unsaved changes"));
        await Expect(Page).ToHaveURLAsync($"{caseUrl}/edit");

        // ⌘/Ctrl+Enter saves.
        await Page.GetByLabel("Steps").PressAsync("Control+Enter");
        await Expect(Page).ToHaveURLAsync(caseUrl);
        await Expect(Page.GetByText("1. Open the page")).ToBeVisibleAsync();

        // Esc cancels (nothing changed, so no question).
        await Page.GotoAsync($"{caseUrl}/edit");
        await Page.GetByLabel("Scenario").FocusAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page).ToHaveURLAsync(caseUrl);

        // Delete opens a confirmation: [Delete test case] [Cancel], says what goes; Cancel backs out.
        await Page.GotoAsync($"{caseUrl}/edit");
        await Footer.GetByRole(AriaRole.Button, new() { Name = "Delete test case" }).ClickAsync();
        await Expect(OpenDialog).ToContainTextAsync($"Delete “{scenario}”?");
        await Expect(OpenDialog).ToContainTextAsync("This can't be undone.");
        var confirm = (await OpenDialog.GetByRole(AriaRole.Button, new() { Name = "Delete test case" }).BoundingBoxAsync())!;
        var back = (await OpenDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).BoundingBoxAsync())!;
        Assert.That(confirm.X, Is.LessThan(back.X), "action first, Cancel to its right — as in the footer");

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "delete-dialog.png") });
        }

        await OpenDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(OpenDialog).ToHaveCountAsync(0);

        await Footer.GetByRole(AriaRole.Button, new() { Name = "Delete test case" }).ClickAsync();
        await OpenDialog.GetByRole(AriaRole.Button, new() { Name = "Delete test case" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases$"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = scenario })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Footer_sits_flush_with_the_bottom_of_its_card()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E flush {Guid.NewGuid():N}");
        await CreateScopeWithCasesAsync(dashboardUrl, $"Scope {Guid.NewGuid():N}");
        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit scope" }).ClickAsync();

        // The card's own bottom padding must not show under the footer (space-y-* used to cancel
        // the footer's negative margin, leaving a gap).
        var gap = await Footer.EvaluateAsync<double>(
            "f => f.parentElement.getBoundingClientRect().bottom - f.getBoundingClientRect().bottom");
        Assert.That(gap, Is.LessThanOrEqualTo(1.5), "only the card's 1px border below the footer");
    }

    [Test]
    public async Task Scope_and_project_deletes_live_in_a_danger_zone_and_project_needs_its_name()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectName = $"E2E danger {suffix}";
        var dashboardUrl = await CreateProjectAsync(projectName);
        await CreateScopeWithCasesAsync(dashboardUrl, $"Scope {suffix}", $"One {suffix}", $"Two {suffix}");

        // Scope: no Delete in the footer; the Danger zone's confirmation counts what goes with it.
        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit scope" }).ClickAsync();
        await Expect(Footer.GetByRole(AriaRole.Button, new() { Name = "Delete scope" })).ToHaveCountAsync(0);
        // The Danger zone starts collapsed: its delete button isn't there to click until it's opened.
        await Expect(Page.Locator("[data-danger-zone]").GetByRole(AriaRole.Button, new() { Name = "Delete scope" })).ToBeHiddenAsync();
        await (await OpenDangerZoneAsync()).GetByRole(AriaRole.Button, new() { Name = "Delete scope" }).ClickAsync();
        await Expect(OpenDialog).ToContainTextAsync("This will delete 2 test cases");
        await OpenDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        // Project: the long form's footer is its own bar at the end of the page (not sticky), Cancel
        // far right; Delete is in the Danger zone.
        await Page.GotoAsync($"{dashboardUrl}/edit");
        await Expect(Footer).Not.ToHaveClassAsync(new Regex(@"\bsticky\b"));
        await Expect(Footer.Locator("[data-form-cancel]")).ToHaveClassAsync(new Regex(@"\bml-auto\b"));
        await Expect(Footer.GetByRole(AriaRole.Button, new() { Name = "Delete project" })).ToHaveCountAsync(0);
        await (await OpenDangerZoneAsync()).GetByRole(AriaRole.Button, new() { Name = "Delete project" }).ClickAsync();
        await Expect(OpenDialog).ToContainTextAsync("This will delete 2 test cases in 1 scope, 0 defects");
        var confirm = OpenDialog.GetByRole(AriaRole.Button, new() { Name = "Delete project" });

        // The button waits for the exact name…
        await Expect(confirm).ToBeDisabledAsync();
        await OpenDialog.Locator("input[name=confirmName]").FillAsync("not the name");
        await Expect(confirm).ToBeDisabledAsync();

        // …and the server checks it too: force a wrong-name submit and nothing is deleted.
        await confirm.EvaluateAsync("b => { b.disabled = false; b.click(); }");
        await Expect(OpenDialog.Locator("[data-confirm-error]")).ToContainTextAsync("doesn't match");
        await Page.GotoAsync(dashboardUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = projectName })).ToBeVisibleAsync();

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.GotoAsync($"{dashboardUrl}/edit");
            await (await OpenDangerZoneAsync()).ScrollIntoViewIfNeededAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "danger-zone.png") });
        }

        await Page.GotoAsync($"{dashboardUrl}/edit");
        await (await OpenDangerZoneAsync()).GetByRole(AriaRole.Button, new() { Name = "Delete project" }).ClickAsync();
        await OpenDialog.Locator("input[name=confirmName]").FillAsync(projectName);
        await Expect(confirm).ToBeEnabledAsync();
        await confirm.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects$"));
        await Page.GotoAsync(dashboardUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = projectName })).ToHaveCountAsync(0);
    }
}
