using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>Scope (S-n) and test case (T-n) IDs show wherever test cases are mentioned.</summary>
[TestFixture]
public class TestCaseIdsTests : E2ETestBase
{
    [Test]
    public async Task Ids_show_on_follow_ups_defects_links_and_activity()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E ids {suffix}");
        var cases = await CreateScopeWithCasesAsync(dashboardUrl, $"Checkout {suffix}", $"Pay by card {suffix}", $"Pay by voucher {suffix}");

        // Failed with no defect raised: the dashboard's follow-up names it by ID.
        await Page.GotoAsync(cases[0]);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Mark as failed" }).ClickAsync();
        await Expect(Page.Locator("summary[aria-label='Change result']")).ToContainTextAsync("Failed");
        await Page.GotoAsync(dashboardUrl);
        await Expect(Page.Locator("[data-follow-up] [data-case-ref]")).ToHaveTextAsync("T-1");

        // A defect: the link picker and the linked list show S- and T- IDs.
        var defectUrl = await RaiseDefectAsync(dashboardUrl, $"Card declined {suffix}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Link test cases" }).ClickAsync();
        var picker = Page.Locator("dialog[open]");
        await Expect(picker.Locator("summary [data-scope-ref]")).ToHaveTextAsync("S-1");
        await Expect(picker.Locator("li [data-case-ref]")).ToHaveTextAsync(["T-1", "T-2"]);
        await picker.Locator("li").Filter(new() { HasTextString = $"Pay by card {suffix}" }).GetByRole(AriaRole.Checkbox).CheckAsync();
        await picker.GetByRole(AriaRole.Button, new() { Name = "Link selected" }).ClickAsync();
        var linked = Page.Locator("#linked-test-cases li");
        await Expect(linked.Locator("[data-scope-ref]")).ToHaveTextAsync("S-1");
        await Expect(linked.Locator("[data-case-ref]")).ToHaveTextAsync("T-1");

        // The defects list shows which cases it's linked to.
        await Page.GotoAsync($"{dashboardUrl}/defects");
        await Expect(Page.Locator("li").Filter(new() { HasTextString = $"Card declined {suffix}" }).Locator("[data-linked-cases]"))
            .ToHaveTextAsync("T-1");

        // The activity feed and the defect's history name the case with its ID.
        await Page.GotoAsync($"{dashboardUrl}/activity");
        await Expect(Page.Locator("[data-history]").GetByRole(AriaRole.Link, new() { Name = $"T-1 Pay by card {suffix}" }).First)
            .ToBeVisibleAsync();
        await Page.GotoAsync(defectUrl);
        await Page.GetByRole(AriaRole.Button, new() { Name = "History" }).ClickAsync();
        await Expect(Page.Locator("dialog[open]")).ToContainTextAsync($"T-1 Pay by card {suffix}");
    }
}
