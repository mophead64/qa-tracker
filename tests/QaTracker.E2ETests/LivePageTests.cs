using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>Live pages (live-page.js): actions post in place, and other people's changes arrive by polling.</summary>
[TestFixture]
public class LivePageTests : E2ETestBase
{
    // The poll interval defaults to 10s; leave room for one full interval plus the refresh.
    private static readonly LocatorAssertionsToBeVisibleOptions AfterAPoll = new() { Timeout = 25_000 };

    /// <summary>Marks the window; it only survives if the page wasn't reloaded.</summary>
    private static Task MarkWindowAsync(IPage page) => page.EvaluateAsync("() => { window.__liveMarker = 42; }");

    private static async Task AssertNotReloadedAsync(IPage page) =>
        Assert.That(await page.EvaluateAsync<int?>("() => window.__liveMarker ?? null"), Is.EqualTo(42),
            "the page was reloaded instead of updated in place");

    [Test]
    public async Task Test_case_actions_update_in_place_and_keep_a_half_typed_comment()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E live {Guid.NewGuid():N}");
        var scenario = $"Live case {Guid.NewGuid():N}";
        await CreateScopeWithCasesAsync(dashboardUrl, $"Live scope {Guid.NewGuid():N}", scenario);
        await MarkWindowAsync(Page);

        // A draft in the comment box survives a result change.
        var draft = "Half-typed note, not posted yet";
        await Page.Locator("#new-comment").FillAsync(draft);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Mark as passed" }).ClickAsync();
        await Expect(Page.Locator("summary[aria-label='Change result']")).ToContainTextAsync("Passed");
        await Expect(Page.Locator("#new-comment")).ToHaveValueAsync(draft);

        await Page.Locator("summary[aria-label='Change result']").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Failed", Exact = true }).ClickAsync();
        await Expect(Page.Locator("summary[aria-label='Change result']")).ToContainTextAsync("Failed");
        await Expect(Page.Locator("#new-comment")).ToHaveValueAsync(draft);

        // Posting it adds the comment and clears the box.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
        await Expect(Page.Locator("#comments li").Filter(new() { HasTextString = draft })).ToBeVisibleAsync();
        await Expect(Page.Locator("#new-comment")).ToHaveValueAsync("");

        // And reacting to it stays put too.
        await Page.Locator("#comments li").Filter(new() { HasTextString = draft })
            .Locator("button[data-reaction='Up']").ClickAsync();
        await Expect(Page.Locator("#comments li").Filter(new() { HasTextString = draft })
            .Locator("button[data-reaction='Up']")).ToHaveAttributeAsync("aria-pressed", "true");

        await AssertNotReloadedAsync(Page);
        await Expect(Page).ToHaveURLAsync(new Regex("/test-cases/[0-9a-fA-F-]{36}$"));
    }

    [Test]
    public async Task Another_tab_sees_results_and_comments_without_reloading()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E live poll {Guid.NewGuid():N}");
        var scenario = $"Polled case {Guid.NewGuid():N}";
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Poll scope {Guid.NewGuid():N}", scenario))[0];

        // A second tab watching the same case, with a draft of its own in progress.
        var watcher = await Context.NewPageAsync();
        await watcher.GotoAsync(caseUrl);
        await MarkWindowAsync(watcher);
        await watcher.Locator("#new-comment").FillAsync("Watcher's draft");
        await watcher.Locator("#new-comment").BlurAsync();

        // Meanwhile, this tab runs the case and comments on it.
        var note = $"From the other tab {Guid.NewGuid():N}";
        await Page.GotoAsync(caseUrl);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Mark as failed" }).ClickAsync();
        await Expect(Page.Locator("summary[aria-label='Change result']")).ToContainTextAsync("Failed");
        await Page.Locator("#new-comment").FillAsync(note);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
        await Expect(Page.Locator("#comments li").Filter(new() { HasTextString = note })).ToBeVisibleAsync();

        await Expect(watcher.Locator("#comments li").Filter(new() { HasTextString = note })).ToBeVisibleAsync(AfterAPoll);
        await Expect(watcher.Locator("summary[aria-label='Change result']")).ToContainTextAsync("Failed");
        await Expect(watcher.Locator("#new-comment")).ToHaveValueAsync("Watcher's draft");
        await AssertNotReloadedAsync(watcher);
        await watcher.CloseAsync();
    }

    [Test]
    public async Task Defect_actions_update_in_place_and_another_tab_follows()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E live defect {Guid.NewGuid():N}");
        var defectUrl = await RaiseDefectAsync(dashboardUrl, $"Live defect {Guid.NewGuid():N}");
        var status = Page.Locator("summary[aria-label='Change status']");

        var watcher = await Context.NewPageAsync();
        await watcher.GotoAsync(defectUrl);
        await MarkWindowAsync(watcher);

        // A draft survives a status change made in place.
        await MarkWindowAsync(Page);
        var draft = "Draft while triaging";
        await Page.Locator("#new-comment").FillAsync(draft);
        await status.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fixing", Exact = true }).ClickAsync();
        await Expect(status).ToContainTextAsync("Fixing");
        await Expect(Page.Locator("#new-comment")).ToHaveValueAsync(draft);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
        await Expect(Page.Locator("#comments li").Filter(new() { HasTextString = draft })).ToBeVisibleAsync();
        await AssertNotReloadedAsync(Page);

        // The other tab picks up both, without reloading.
        await Expect(watcher.Locator("#comments li").Filter(new() { HasTextString = draft })).ToBeVisibleAsync(AfterAPoll);
        await Expect(watcher.Locator("summary[aria-label='Change status']")).ToContainTextAsync("Fixing");
        await AssertNotReloadedAsync(watcher);
        await watcher.CloseAsync();
    }

    [Test]
    public async Task Defects_list_shows_new_defects_raised_elsewhere()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E live defects {Guid.NewGuid():N}");
        await RaiseDefectAsync(dashboardUrl, $"Existing defect {Guid.NewGuid():N}");

        var watcher = await Context.NewPageAsync();
        await watcher.GotoAsync($"{dashboardUrl}/defects");
        await MarkWindowAsync(watcher);

        var raised = $"Raised elsewhere {Guid.NewGuid():N}";
        await RaiseDefectAsync(dashboardUrl, raised);

        await Expect(watcher.GetByRole(AriaRole.Link, new() { Name = raised })).ToBeVisibleAsync(AfterAPoll);
        await AssertNotReloadedAsync(watcher);
        await watcher.CloseAsync();
    }

    [Test]
    public async Task Test_plan_quick_results_post_in_place_and_other_tabs_follow()
    {
        var dashboardUrl = await CreateProjectAsync($"E2E live plan {Guid.NewGuid():N}");
        var scenario = $"Plan case {Guid.NewGuid():N}";
        await CreateScopeWithCasesAsync(dashboardUrl, $"Plan scope {Guid.NewGuid():N}", scenario);

        var watcher = await Context.NewPageAsync();
        await watcher.GotoAsync($"{dashboardUrl}/test-cases");
        await MarkWindowAsync(watcher);

        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        await MarkWindowAsync(Page);
        var row = Page.Locator("li").Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = scenario }) });
        await row.GetByRole(AriaRole.Button, new() { Name = "Pass", Exact = true }).ClickAsync();
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Pass", Exact = true })).ToBeDisabledAsync();
        await AssertNotReloadedAsync(Page);

        var watched = watcher.Locator("li").Filter(new() { Has = watcher.GetByRole(AriaRole.Link, new() { Name = scenario }) });
        await Expect(watched.GetByText("Passed", new() { Exact = true })).ToBeVisibleAsync(AfterAPoll);
        await AssertNotReloadedAsync(watcher);
        await watcher.CloseAsync();
    }
}
