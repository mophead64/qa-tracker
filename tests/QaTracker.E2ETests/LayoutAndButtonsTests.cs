using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>App-wide UI: the collapsible side nav, button order (positive action first; on a
/// delete, Cancel first), and the ⓘ tips on create forms.</summary>
[TestFixture]
public class LayoutAndButtonsTests : E2ETestBase
{
    [Test]
    public async Task Side_nav_collapses_to_icons_and_stays_collapsed()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E nav {Guid.NewGuid():N}");
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoAsync(dashboardUrl);
        var nav = Page.Locator("[data-side-nav]");
        var toggle = Page.GetByRole(AriaRole.Button, new() { Name = "Collapse navigation" });

        var wide = (await nav.BoundingBoxAsync())!.Width;
        await toggle.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Expand navigation" })).ToHaveAttributeAsync("aria-expanded", "false");
        var narrow = wide;
        for (var i = 0; i < 50 && narrow >= wide / 2; i++) // width animates — give it a moment
        {
            await Page.WaitForTimeoutAsync(100);
            narrow = (await nav.BoundingBoxAsync())!.Width;
        }
        Assert.That(narrow, Is.LessThan(wide / 2), "collapsed nav should shrink to an icon rail");

        // Icons only, but the links keep their names (and still work).
        // (Visually hidden, not removed: the label shrinks to a 1px screen-reader-only box.)
        Assert.That((await nav.GetByText("Test cases", new() { Exact = true }).BoundingBoxAsync())!.Width, Is.LessThanOrEqualTo(1));
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Test cases" })).ToBeVisibleAsync();

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await nav.GetByRole(AriaRole.Link, new() { Name = "Test cases" }).HoverAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "nav-collapsed.png") });
        }

        // It stays collapsed across in-app navigation and a full reload (cookie, rendered by the server).
        await nav.GetByRole(AriaRole.Link, new() { Name = "Test cases" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases$"));
        await Expect(Page.Locator("[data-nav-root]")).ToHaveClassAsync(new Regex(@"\bnav-collapsed\b"));
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-nav-root]")).ToHaveClassAsync(new Regex(@"\bnav-collapsed\b"));

        // And back.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Expand navigation" }).ClickAsync();
        Assert.That((await nav.GetByText("Test cases", new() { Exact = true }).BoundingBoxAsync())!.Width, Is.GreaterThan(20));
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-nav-root]")).Not.ToHaveClassAsync(new Regex(@"\bnav-collapsed\b"));
        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } shots)
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(shots, "nav-expanded.png") });
        }
    }

    [Test]
    public async Task Create_forms_lead_with_the_action_and_deletes_lead_with_cancel()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E buttons {Guid.NewGuid():N}");

        // Create: [Create scope] then Cancel, and a tip about what comes next.
        await Page.GotoAsync($"{dashboardUrl}/test-cases/scopes/new");
        var create = (await Page.GetByRole(AriaRole.Button, new() { Name = "Create scope" }).BoundingBoxAsync())!;
        var cancel = (await Page.GetByRole(AriaRole.Link, new() { Name = "Cancel" }).BoundingBoxAsync())!;
        Assert.That(create.X, Is.LessThan(cancel.X), "the positive action comes first");
        await Expect(Page.Locator("[data-form-tip]")).ToContainTextAsync("A scope groups related test cases");

        // Delete: Cancel first, the destructive button last — and Cancel backs out.
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Scope {Guid.NewGuid():N}", $"Case {Guid.NewGuid():N}"))[0];
        await Page.GotoAsync($"{caseUrl}/edit");
        await Expect(Page.Locator("[data-form-tip]")).ToHaveCountAsync(0); // tips are for creating
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete test case" }).ClickAsync();
        var keep = (await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).BoundingBoxAsync())!;
        var delete = (await Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).BoundingBoxAsync())!;
        Assert.That(keep.X, Is.LessThan(delete.X), "on a delete the safe choice comes first");
        await Expect(Page.GetByText("This can't be undone.")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" })).ToBeHiddenAsync();

        // Modals: the positive action first, then Cancel (the team "Add" modal).
        await Page.GotoAsync(dashboardUrl);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        var modal = Page.Locator("dialog[open]");
        var add = (await modal.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).BoundingBoxAsync())!;
        var modalCancel = (await modal.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).BoundingBoxAsync())!;
        Assert.That(add.X, Is.LessThan(modalCancel.X));
    }
}
