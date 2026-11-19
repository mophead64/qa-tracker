using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// The 2026-11-14 consistency rules: nothing runs off a phone screen, every page has the same
/// header (breadcrumb trail, title, actions) and the same "not found" state, and edit forms name
/// the item they edit.
/// </summary>
[TestFixture]
public class ConsistencyTests : E2ETestBase
{
    /// <summary>Elements inside the page that end past the viewport's right edge (hidden tooltips aside).</summary>
    private Task<string[]> OverflowingAsync() => Page.EvaluateAsync<string[]>(@"() => {
        const vw = document.documentElement.clientWidth;
        const out = [];
        document.querySelectorAll('main *').forEach(el => {
            const b = el.getBoundingClientRect();
            if (b.width === 0 || b.right <= vw + 1) return;
            if (el.closest('.link-group-tip, details:not([open]), dialog:not([open]), [hidden]')) return;
            for (let a = el.parentElement; a && a.tagName !== 'MAIN'; a = a.parentElement) {
                const o = getComputedStyle(a).overflowX;
                if (['hidden', 'auto', 'scroll', 'clip'].includes(o) && a.getBoundingClientRect().right <= vw + 1) return;
            }
            out.push(el.tagName + ' ' + (el.textContent || '').trim().slice(0, 40));
        });
        return out.slice(0, 5);
    }");

    [Test]
    public async Task Nothing_runs_off_a_phone_screen()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E phone {suffix}");
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Phone scope {suffix}",
            $"A deliberately long scenario name that would squeeze the quick actions {suffix}", $"Second case {suffix}"))[0];
        var defectUrl = await RaiseDefectAsync(dashboardUrl, $"Phone defect {suffix}");

        await Page.SetViewportSizeAsync(390, 844);
        foreach (var url in new[] { dashboardUrl, $"{dashboardUrl}/test-cases", caseUrl, $"{dashboardUrl}/defects", defectUrl,
                     $"{BaseUrl}/projects", $"{BaseUrl}/admin/users", $"{BaseUrl}/settings" })
        {
            await Page.GotoAsync(url);
            await Expect(Page.Locator("main h1").First).ToBeVisibleAsync();
            Assert.That(await OverflowingAsync(), Is.Empty, $"content runs off the screen on {url}");
        }

        // An opened status menu is nudged back onto the screen (keep-in-view.js).
        await Page.GotoAsync(defectUrl);
        await Page.Locator("[data-field=status] summary").ClickAsync();
        var panel = Page.Locator("[data-field=status] details[open] > div").First;
        await Expect(panel).ToBeVisibleAsync();
        // The nudge runs on the details "toggle" event, a moment after it opens.
        await Page.WaitForFunctionAsync(@"() => {
            const p = document.querySelector('[data-field=status] details[open] > div');
            const b = p && p.getBoundingClientRect();
            return b && b.left >= 0 && b.right <= document.documentElement.clientWidth;
        }", null, new() { Timeout = 3000 });
    }

    [Test]
    public async Task Pages_share_a_breadcrumb_header_and_forms_name_the_item()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var project = $"E2E crumbs {suffix}";
        var dashboardUrl = await CreateProjectAsync(project);
        var scope = $"Crumb scope {suffix}";
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, scope, $"Crumb case {suffix}"))[0];
        var defectUrl = await RaiseDefectAsync(dashboardUrl, $"Crumb defect {suffix}");
        var crumbs = Page.Locator("[data-breadcrumb]");

        await Page.GotoAsync(caseUrl);
        await Expect(crumbs.GetByRole(AriaRole.Link)).ToHaveTextAsync(new[]
        {
            new Regex($"^{Regex.Escape(project)}$"), new Regex("^Test cases$"), new Regex($"^S-\\d+ {Regex.Escape(scope)}$"),
        });
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(new Regex(@"^\s*Edit test case\s*T-\d+\s*$"));

        await Page.GotoAsync(defectUrl);
        await Expect(crumbs).ToContainTextAsync(new Regex(@"Defects\s*/\s*D-\d+", RegexOptions.IgnoreCase));
        await Page.GotoAsync($"{defectUrl}/edit");
        await Expect(crumbs.GetByRole(AriaRole.Link)).ToHaveTextAsync(new[] { project, "Defects" });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(new Regex(@"^\s*Edit defect\s*D-\d+\s*$"));

        await Page.GotoAsync($"{BaseUrl}/admin/users/new");
        await Expect(crumbs.GetByRole(AriaRole.Link)).ToHaveTextAsync(new[] { "System settings", "Users" });
    }

    [Test]
    public async Task Missing_items_say_what_is_missing_and_link_back()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E not found {Guid.NewGuid():N}");
        var missing = Guid.NewGuid();

        foreach (var (url, title, back) in new[]
                 {
                     ($"{dashboardUrl}/defects/{missing}", "Defect not found", "Back to defects"),
                     ($"{dashboardUrl}/defects/{missing}/edit", "Defect not found", "Back to defects"),
                     ($"{dashboardUrl}/test-cases/{missing}", "Test case not found", "Back to test cases"),
                     ($"{BaseUrl}/projects/{missing}", "Project not found", "Back to all projects"),
                 })
        {
            await Page.GotoAsync(url);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-not-found]")).ToContainTextAsync("It may have been deleted");
            await Expect(Page.Locator("[data-not-found]").GetByRole(AriaRole.Link, new() { Name = back })).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task The_side_nav_keeps_its_section_lit_on_every_page_under_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E nav {suffix}");   // also makes it the current project
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Nav scope {suffix}", $"Nav case {suffix}"))[0];
        var defectUrl = await RaiseDefectAsync(dashboardUrl, $"Nav defect {suffix}");

        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        var scopeUrl = BaseUrl + "/" + await Page.Locator("main section a[href*='/scopes/']").First.GetAttributeAsync("href");
        var lit = Page.Locator("[data-side-nav] a[aria-current='page']");

        foreach (var (url, entry) in new[]
                 {
                     (dashboardUrl, "Dashboard"), ($"{dashboardUrl}/edit", "Dashboard"), ($"{dashboardUrl}/activity", "Dashboard"),
                     ($"{dashboardUrl}/test-cases", "Test cases"), (caseUrl, "Test cases"), ($"{caseUrl}/edit", "Test cases"),
                     (scopeUrl, "Test cases"), ($"{scopeUrl}/edit", "Test cases"), ($"{scopeUrl}/test-cases/new", "Test cases"),
                     ($"{dashboardUrl}/scopes/new", "Test cases"), ($"{dashboardUrl}/test-cases/import", "Test cases"),
                     ($"{dashboardUrl}/defects", "Defects"), (defectUrl, "Defects"), ($"{defectUrl}/edit", "Defects"),
                     ($"{dashboardUrl}/defects/new", "Defects"),
                     ($"{BaseUrl}/projects", "All projects"), ($"{BaseUrl}/projects/new", "All projects"),
                     ($"{BaseUrl}/admin/users", "System settings"),
                 })
        {
            await Page.GotoAsync(url);
            await Expect(lit).ToHaveCountAsync(1);
            await Expect(lit).ToHaveAttributeAsync("title", entry);
            Assert.That(await lit.GetAttributeAsync("class"), Does.Contain("nav-side-active"), url);
        }
    }
}
