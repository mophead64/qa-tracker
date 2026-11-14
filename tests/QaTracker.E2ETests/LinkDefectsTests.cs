using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The test case page's "Link or create defect" modal: newest first, filtered by status.</summary>
[TestFixture]
public class LinkDefectsTests : E2ETestBase
{
    private async Task<string> RaiseAsync(string dashboardUrl, string summary)
    {
        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));
        return Page.Url;
    }

    /// <summary>JS: whether an element actually scrolls — try it, and put it back.</summary>
    private const string CanScroll = "el => { el.scrollTop = 50; const moved = el.scrollTop > 0; el.scrollTop = 0; return moved; }";

    private async Task SetStatusAsync(string label)
    {
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change status']").Filter(new() { HasTextString = label }));
    }

    [Test]
    public async Task Link_modal_lists_newest_first_and_filters_by_status()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E link filter {suffix}");

        // One defect in every status (D-1 … D-6), so the filter offers all eight options.
        string first = $"First bug {suffix}", closed = $"Closed bug {suffix}", newest = $"Newest bug {suffix}";
        await RaiseAsync(dashboardUrl, first);
        await RaiseAsync(dashboardUrl, closed);
        await SetStatusAsync("Not a defect");
        await RaiseAsync(dashboardUrl, $"Fixing bug {suffix}");
        await SetStatusAsync("Fixing");
        await RaiseAsync(dashboardUrl, $"To check bug {suffix}");
        await SetStatusAsync("To check");
        var fixedBug = $"Fixed bug {suffix}";
        await RaiseAsync(dashboardUrl, fixedBug);
        await SetStatusAsync("Fixed");
        await RaiseAsync(dashboardUrl, newest);

        var scenario = $"Linking case {suffix}";
        await CreateScopeWithCasesAsync(dashboardUrl, $"Link scope {suffix}", scenario);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Link or create defect" }).ClickAsync();
        var modal = Page.Locator("#link-defects-modal");
        var filter = modal.Locator("summary[aria-label='Show defects']");
        var shown = modal.Locator("li[data-filter-tags]:not([hidden])");

        // Open defects (not fixed, fixing, to check) by default, newest number first.
        await Expect(filter).ToContainTextAsync("Open (4)");
        await Expect(shown).ToHaveCountAsync(4);
        await Expect(shown.Nth(0)).ToContainTextAsync("D-6");
        await Expect(shown.Nth(0)).ToContainTextAsync(newest);
        await Expect(shown.Nth(3)).ToContainTextAsync(first);

        // Ticks survive changing the filter.
        await shown.Nth(0).GetByRole(AriaRole.Checkbox).CheckAsync();

        // All eight options show at once: neither the open list nor the modal scrolls.
        await filter.ClickAsync();
        await Expect(modal.Locator("[data-select-option]")).ToHaveCountAsync(8);
        var scrolls = await modal.EvaluateAsync<bool[]>($"d => [d.querySelector('details[data-form-select] > div'), d].map({CanScroll})");
        Assert.That(scrolls, Is.EqualTo(new[] { false, false }), "[option list scrolls, modal scrolls]");

        await modal.GetByRole(AriaRole.Button, new() { Name = "All defects (6)" }).ClickAsync();
        await Expect(shown).ToHaveCountAsync(6);
        await Expect(shown.Nth(1)).ToContainTextAsync(fixedBug);

        // Narrowed to one defect the modal is at its shortest — the open list still sits on top,
        // whole, hanging over the modal's edge rather than scrolling inside it.
        await filter.ClickAsync();
        await modal.GetByRole(AriaRole.Button, new() { Name = "Fixed (1)" }).ClickAsync();
        await Expect(shown).ToHaveCountAsync(1);
        await filter.ClickAsync();
        var last = modal.Locator("[data-select-option]").Last;
        await Expect(last).ToBeVisibleAsync();
        var onTop = await last.EvaluateAsync<bool>(@"el => {
            const r = el.getBoundingClientRect();
            const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
            return !!hit && el.contains(hit) && r.bottom <= window.innerHeight;
        }");
        Assert.That(onTop, Is.True, "the dropdown's last option is covered or off screen");
        Assert.That(await modal.EvaluateAsync<bool>(CanScroll), Is.False, "the modal scrolls");

        await modal.GetByRole(AriaRole.Button, new() { Name = "Closed (2)" }).ClickAsync();
        await Expect(shown).ToHaveCountAsync(2);
        await Expect(shown.Nth(0)).ToContainTextAsync(fixedBug);
        await Expect(shown.Nth(1)).ToContainTextAsync(closed);
        await shown.Nth(1).GetByRole(AriaRole.Checkbox).CheckAsync();

        // Both ticked defects are linked, including the one the filter had hidden.
        await modal.GetByRole(AriaRole.Button, new() { Name = "Link selected" }).ClickAsync();
        var linked = Page.Locator("#linked-defects li");
        await Expect(linked).ToHaveCountAsync(2);
        await Expect(linked.Filter(new() { HasTextString = newest })).ToBeVisibleAsync();
        await Expect(linked.Filter(new() { HasTextString = closed })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Defect_side_link_picker_groups_cases_in_collapsible_scopes_with_links()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E link picker {token}");
        var scopeA = $"Alpha {token}";
        var scopeB = $"Bravo {token}";
        await CreateScopeWithCasesAsync(dashboardUrl, scopeA, $"A one {token}");
        await CreateScopeWithCasesAsync(dashboardUrl, scopeB, $"B one {token}", $"B two {token}");
        var defectUrl = await RaiseDefectAsync(dashboardUrl, $"Picker defect {token}");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Link test cases" }).ClickAsync();
        var modal = Page.Locator("dialog[open]");
        var groups = modal.Locator("details[data-link-scope]");
        await Expect(groups).ToHaveCountAsync(2);
        var bravo = modal.Locator($"details[data-link-scope='{scopeB}']");
        await Expect(bravo.Locator("summary")).ToContainTextAsync("2 cases");

        // Collapse / expand them all.
        await modal.GetByRole(AriaRole.Button, new() { Name = "Collapse all" }).ClickAsync();
        await Expect(modal.Locator("details[data-link-scope][open]")).ToHaveCountAsync(0);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();
        await Expect(modal.Locator("details[data-link-scope][open]")).ToHaveCountAsync(2);

        // "Select all" ticks a scope's cases (and flips to Clear) without collapsing it.
        await bravo.Locator("[data-check-all]").ClickAsync();
        await Expect(bravo.GetByRole(AriaRole.Checkbox, new() { Checked = true })).ToHaveCountAsync(2);
        await Expect(bravo.Locator("[data-check-all]")).ToHaveTextAsync("Clear");
        await Expect(bravo).ToHaveAttributeAsync("open", "");

        // The scope's name goes to its own page.
        await Expect(bravo.Locator("[data-scope-link]")).ToHaveAttributeAsync("href", new Regex("/scopes/[0-9a-fA-F-]{36}$"));

        await modal.GetByRole(AriaRole.Button, new() { Name = "Link selected" }).ClickAsync();
        var linked = Page.Locator("#linked-test-cases li");
        await Expect(linked).ToHaveCountAsync(2);

        // In the linked list too: the scope name opens that scope's test cases.
        await linked.First.Locator("[data-scope-link]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/scopes/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = scopeB })).ToBeVisibleAsync();
    }
}
