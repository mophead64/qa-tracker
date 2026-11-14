using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>Scopes list in creation order and can be rearranged from the "Reorder scopes" modal.</summary>
[TestFixture]
public class ScopeOrderTests : E2ETestBase
{
    [Test]
    public async Task Scopes_list_in_creation_order_and_can_be_reordered()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E scope order {Guid.NewGuid():N}");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        // Deliberately not alphabetical, so creation order is distinguishable from sorting by name.
        string zulu = $"Zulu {suffix}", mike = $"Mike {suffix}", alpha = $"Alpha {suffix}";
        await CreateScopeWithCasesAsync(dashboardUrl, zulu);
        await CreateScopeWithCasesAsync(dashboardUrl, mike);
        await CreateScopeWithCasesAsync(dashboardUrl, alpha);

        var listUrl = $"{dashboardUrl}/test-cases";
        await Page.GotoAsync(listUrl);
        Assert.That(await PanelOrderAsync(), Is.EqualTo(new[] { zulu, mike, alpha }));

        var modal = Page.Locator("#reorder-scopes-modal");
        var rows = modal.Locator("[data-reorder-item]");

        // Arrow buttons: move Alpha up one (Zulu, Alpha, Mike)…
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reorder scopes" }).ClickAsync();
        await Expect(modal).ToBeVisibleAsync();
        await Expect(modal.GetByRole(AriaRole.Button, new() { Name = $"Move {zulu} up" })).ToBeDisabledAsync();
        await modal.GetByRole(AriaRole.Button, new() { Name = $"Move {alpha} up" }).ClickAsync();
        await Expect(modal.Locator("[data-reorder-name]")).ToHaveTextAsync([zulu, alpha, mike]);

        // …then drag Mike to the top with the mouse (Mike, Zulu, Alpha). The modal stays open.
        await DragAsync(rows.Nth(2), rows.Nth(0));
        await Expect(modal.Locator("[data-reorder-name]")).ToHaveTextAsync([mike, zulu, alpha]);
        await Expect(modal).ToBeVisibleAsync();

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "scope-reorder.png") });
        }

        // Cancel throws the changes away, on the page and in the modal.
        await modal.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(modal).Not.ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reorder scopes" }).ClickAsync();
        await Expect(modal.Locator("[data-reorder-name]")).ToHaveTextAsync([zulu, mike, alpha]);

        // Do it again and save: the page (and a reload) shows the new order.
        await DragAsync(rows.Nth(2), rows.Nth(0));
        await Expect(modal.Locator("[data-reorder-name]")).ToHaveTextAsync([alpha, zulu, mike]);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save order" }).ClickAsync();
        await Expect(modal).Not.ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases$"));
        Assert.That(await PanelOrderAsync(), Is.EqualTo(new[] { alpha, zulu, mike }));

        await Page.ReloadAsync();
        Assert.That(await PanelOrderAsync(), Is.EqualTo(new[] { alpha, zulu, mike }));
    }

    private async Task<IReadOnlyList<string>> PanelOrderAsync() =>
        (await Page.Locator("section[id^='scope-'] h2 > a").AllInnerTextsAsync())
            .Select(t => t.Trim()).ToList();

    /// <summary>Real pointer drag: press on the source row, glide to the top edge of the target, release.</summary>
    private async Task DragAsync(ILocator source, ILocator target)
    {
        var from = (await source.Locator("[data-reorder-name]").BoundingBoxAsync())!;
        var to = (await target.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(from.X + from.Width / 2, from.Y + from.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(to.X + to.Width / 2, to.Y + 2, new() { Steps = 10 });
        await Page.Mouse.UpAsync();
    }
}
