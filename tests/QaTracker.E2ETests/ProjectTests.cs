using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for project creation.</summary>
[TestFixture]
public class ProjectTests : E2ETestBase
{
    [Test]
    public async Task Qa_can_create_a_project_with_a_custom_link_and_switch_to_it()
    {
        var name = $"E2E project {Guid.NewGuid():N}";

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.GetByRole(AriaRole.Link, new() { Name = "New project" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/new$"));

        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync(),
            Page.GetByPlaceholder("Label").First);

        await Page.GetByLabel("Name").FillAsync(name);
        await Page.GetByLabel("Notes").FillAsync("Created by the e2e test.");
        await Page.GetByPlaceholder("Label").First.FillAsync("Repo");
        await Page.GetByPlaceholder("https://…").First.FillAsync("https://example.test/repo");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create project" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/[0-9a-fA-F-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();
        await Expect(Page.Locator("summary[aria-label='Change project status']").GetByText("Not Started")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Repo" })).ToBeVisibleAsync();

        await Expect(Page.Locator("header summary").Filter(new() { HasTextString = name })).ToBeVisibleAsync();

        var dashboardUrl = Page.Url;

        // The status dropdown submits on pick — move it to Completed.
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change project status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Completed" }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change project status']").Filter(new() { HasTextString = "Completed" }));

        await Page.GotoAsync($"{BaseUrl}/");
        await Expect(Page).ToHaveURLAsync(dashboardUrl);
    }

    [Test]
    public async Task Testing_schedule_shows_qa_days_beside_the_status()
    {
        var name = $"E2E schedule {Guid.NewGuid():N}";
        var dashboardUrl = await CreateProjectAsync(name);
        var qaDays = Page.Locator("[data-qa-days]");

        // Mon 9 – Wed 11 Nov, ending at midday: 2.5 weekdays of testing.
        await Page.GotoAsync($"{dashboardUrl}/edit");
        await Page.GetByLabel("Testing start date").FillAsync("2026-11-11");
        await Page.GetByLabel("Testing end date").FillAsync("2026-11-09");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page.Locator("[data-schedule-error]")).ToContainTextAsync("can't be before the start date");

        await Page.GetByLabel("Testing start date").FillAsync("2026-11-09");
        await Page.GetByLabel("Testing end date").FillAsync("2026-11-11");
        await Page.GetByLabel("Ends at midday").CheckAsync();
        await Page.GetByLabel("Estimated dev days").FillAsync("10");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(dashboardUrl);
        await Expect(qaDays).ToHaveTextAsync("2.5 days");
        await Expect(Page.Locator("[data-schedule]")).ToContainTextAsync("Testing Mon 9 Nov 2026 – Wed 11 Nov 2026 (until midday)");
        await Expect(Page.Locator("[data-schedule]")).ToContainTextAsync("10 days dev → 2.5 days QA (25%, system default)");

        // The projects list shows the window's dates between the status and the days.
        await Page.GotoAsync($"{BaseUrl}/projects?q={Uri.EscapeDataString(name)}");
        var datedRow = Page.Locator("li").Filter(new() { HasTextString = name });
        var datesText = DateTime.UtcNow.Year == 2026 ? "9 Nov – 11 Nov" : "9 Nov 2026 – 11 Nov 2026";
        await Expect(datedRow.Locator("[data-testing-dates]")).ToHaveTextAsync(datesText);
        await Expect(datedRow.Locator("[data-qa-days]")).ToHaveTextAsync("2.5 days");

        // Without dates the label falls back to the estimate — here 10 dev days at 40%.
        await Page.GotoAsync($"{dashboardUrl}/edit");
        await Page.GetByLabel("Testing start date").FillAsync("");
        await Page.GetByLabel("Testing end date").FillAsync("");
        await Page.GetByLabel("QA % of dev").FillAsync("40");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(dashboardUrl);
        await Expect(qaDays).ToHaveTextAsync("~4 days");

        // And the same label sits beside the status on the projects list (searched for, so a
        // long Not Started section can't page it out of view).
        await Page.GotoAsync($"{BaseUrl}/projects?q={Uri.EscapeDataString(name)}");
        var row = Page.Locator("li").Filter(new() { HasTextString = name });
        await Expect(row.Locator("[data-qa-days]")).ToHaveTextAsync("~4 days");
    }

    [Test]
    public async Task Testing_dates_make_a_project_upcoming_and_it_can_be_cancelled()
    {
        var name = $"E2E upcoming {Guid.NewGuid():N}";
        var dashboardUrl = await CreateProjectAsync(name);
        var status = Page.Locator("summary[aria-label='Change project status']");
        await Expect(status).ToContainTextAsync("Not Started");

        await Page.GotoAsync($"{dashboardUrl}/edit");
        await Page.GetByLabel("Testing start date").FillAsync("2026-11-16");
        await Page.GetByLabel("Testing end date").FillAsync("2026-11-20");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(dashboardUrl);
        await Expect(status).ToContainTextAsync("Upcoming");

        await RetryUntil(
            async () =>
            {
                await status.ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Cancelled", Exact = true }).ClickAsync();
            },
            status.Filter(new() { HasTextString = "Cancelled" }));

        // Both changes are in the project's activity.
        var activity = Page.Locator("[data-recent-activity] [data-history] > li");
        await Expect(activity.Nth(0)).ToContainTextAsync("moved the project from Upcoming to Cancelled");
        await Expect(activity.Nth(1)).ToContainTextAsync("edited the project's testing schedule");
        await Expect(activity.Nth(2)).ToContainTextAsync("moved the project from Not Started to Upcoming");
    }

    [Test]
    public async Task Upcoming_projects_are_listed_soonest_first()
    {
        // "A later" sorts first by name, so the order below can only come from the start dates.
        var token = Guid.NewGuid().ToString("N");
        var later = $"E2E order {token} A later";
        var sooner = $"E2E order {token} B sooner";

        foreach (var (name, start, end) in new[] { (later, "2030-03-04", "2030-03-08"), (sooner, "2030-01-07", "2030-01-11") })
        {
            var dashboardUrl = await CreateProjectAsync(name);
            await Page.GotoAsync($"{dashboardUrl}/edit");
            await Page.GetByLabel("Testing start date").FillAsync(start);
            await Page.GetByLabel("Testing end date").FillAsync(end);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(dashboardUrl);
        }

        await Page.GotoAsync($"{BaseUrl}/projects?q={token}");
        var upcoming = Page.Locator("h2").Filter(new() { HasTextString = "Upcoming" }).Locator("+ ul > li");
        await Expect(upcoming).ToHaveCountAsync(2);
        await Expect(upcoming.Nth(0)).ToContainTextAsync(sooner);
        await Expect(upcoming.Nth(1)).ToContainTextAsync(later);
    }

    [Test]
    public async Task Switcher_shows_each_projects_status()
    {
        var name = $"E2E status switcher {Guid.NewGuid():N}";
        await CreateProjectAsync(name);

        var switcher = Page.Locator("header summary").Filter(new() { HasTextString = name });
        var row = Page.Locator("header form[action='projects/switch'] button").Filter(new() { HasTextString = name });
        await switcher.ClickAsync();
        await Expect(row.Locator("[data-project-status]")).ToHaveTextAsync("Not Started");

        // It follows a status change made on the dashboard.
        await Page.Keyboard.PressAsync("Escape");
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change project status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Active", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change project status']").Filter(new() { HasTextString = "Active" }));
        await switcher.ClickAsync();
        await Expect(row.Locator("[data-project-status]")).ToHaveTextAsync("Active");
    }

    [Test]
    public async Task Project_switcher_follows_the_project_list_filter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var activeName = $"Switcher active {suffix}";
        var notStartedName = $"Switcher fresh {suffix}";
        var devEmail = $"e2e-switcher-{suffix}@test.local";

        // One project moved to Active, one left Not Started. A dedicated user, so changing
        // their saved filter can't affect any other test's view of the project list.
        await CreateProjectAsync(activeName);
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change project status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Active", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change project status']").Filter(new() { HasTextString = "Active" }));
        await CreateProjectAsync(notStartedName);
        await CreateUserAsync(devEmail, $"Switcher {suffix}", "Dev", DevPassword);

        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);
        ILocator SwitcherItem(string name) =>
            devPage.Locator("header form[action='projects/switch'] button").Filter(new() { HasTextString = name });

        // Default filter (Active & Upcoming): both are offered.
        await devPage.GotoAsync($"{BaseUrl}/projects");
        await Expect(SwitcherItem(activeName)).ToHaveCountAsync(1);
        await Expect(SwitcherItem(notStartedName)).ToHaveCountAsync(1);

        // Filter to Active only: the switcher drops the Not Started project too.
        await RetryUntil(
            async () =>
            {
                await devPage.Locator("summary[aria-label='Filter projects']").ClickAsync();
                await devPage.GetByRole(AriaRole.Button, new() { Name = "Active", Exact = true }).ClickAsync();
            },
            devPage.Locator("summary[aria-label='Filter projects']").Filter(new() { HasTextString = "Active" }));
        await Expect(SwitcherItem(activeName)).ToHaveCountAsync(1);
        await Expect(SwitcherItem(notStartedName)).ToHaveCountAsync(0);

        await DeleteUserAsync(devEmail);
    }

    [Test]
    public async Task Active_projects_show_test_result_tags_on_the_project_list()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var activeName = $"Tagged active {suffix}";
        var freshName = $"Tagged fresh {suffix}";
        var scopeName = $"Scope {suffix}";

        // The first scope makes the project Active. Two cases: one failed, one never run.
        var dashboardUrl = await CreateProjectAsync(activeName);
        var caseUrls = await CreateScopeWithCasesAsync(dashboardUrl, scopeName, $"Never run {suffix}", $"Fails {suffix}");

        // Nothing has failed yet, so there's no "failed" tag at all.
        await Page.GotoAsync($"{BaseUrl}/projects?q={suffix}");
        await Expect(Page.Locator("li").Filter(new() { HasTextString = activeName }).Locator("[data-test-tags] > span"))
            .ToHaveTextAsync(["0 tests passed", "2 tests not run/blocked"]);

        await SetResultAsync(caseUrls[1], "Failed");

        await CreateProjectAsync(freshName);

        await Page.GotoAsync($"{BaseUrl}/projects?q={suffix}");
        var activeRow = Page.Locator("li").Filter(new() { HasTextString = activeName });
        var tags = activeRow.Locator("[data-test-tags] > span");
        await Expect(tags).ToHaveTextAsync(["0 tests passed", "1 test failed", "1 test not run/blocked"]);

        // Not Started projects get no test tags.
        await Expect(Page.Locator("li").Filter(new() { HasTextString = freshName }).Locator("[data-test-tags]"))
            .ToHaveCountAsync(0);

        // Once every case has a result, the "not run/blocked" tag goes too.
        await SetResultAsync(caseUrls[0], "Passed");
        await Page.GotoAsync($"{BaseUrl}/projects?q={suffix}");
        await Expect(tags).ToHaveTextAsync(["1 test passed", "1 test failed"]);
    }

    [Test]
    public async Task Active_projects_show_open_and_to_verify_defect_counts_on_the_project_list()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Defect tags {suffix}";
        var dashboardUrl = await CreateProjectAsync(name);
        await CreateScopeWithCasesAsync(dashboardUrl, $"Scope {suffix}", $"Case {suffix}");

        var row = Page.Locator("li").Filter(new() { HasTextString = name });
        await Page.GotoAsync($"{BaseUrl}/projects?q={suffix}");
        await Expect(row.Locator("[data-defect-tags]")).ToHaveCountAsync(0);

        // Not fixed + Fixing count as open; To check is waiting on QA; Fixed counts as neither.
        await RaiseDefectAsync(dashboardUrl, $"Not fixed {suffix}");
        await RaiseDefectAsync(dashboardUrl, $"Fixing {suffix}", status: "Fixing");
        await RaiseDefectAsync(dashboardUrl, $"To check {suffix}", status: "To check");
        await RaiseDefectAsync(dashboardUrl, $"Fixed {suffix}", status: "Fixed");

        await Page.GotoAsync($"{BaseUrl}/projects?q={suffix}");
        await Expect(row.Locator("[data-defects-open]")).ToHaveTextAsync("2 defects open");
        await Expect(row.Locator("[data-defects-open]")).ToHaveAttributeAsync("title", "1 not fixed · 1 fixing");
        await Expect(row.Locator("[data-defects-to-verify]")).ToHaveTextAsync("1 defect to verify");
    }

    [Test]
    public async Task Completing_a_project_with_outstanding_work_warns_first()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var openDefect = $"Still broken {suffix}";
        var dismissedDefect = $"Not really broken {suffix}";
        var dashboardUrl = await CreateProjectAsync($"Completion warning {suffix}");
        var caseUrls = await CreateScopeWithCasesAsync(dashboardUrl, $"Scope {suffix}", $"Fails {suffix}");
        await SetResultAsync(caseUrls[0], "Failed");

        // One defect left open, one dismissed as "Not a defect" (which doesn't count).
        foreach (var summary in new[] { openDefect, dismissedDefect })
        {
            await Page.GotoAsync($"{dashboardUrl}/defects/new");
            await SubmitUntil(
                async () =>
                {
                    await Page.GetByLabel("Summary").FillAsync(summary);
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
                },
                Page.GetByRole(AriaRole.Heading, new() { Name = summary }));
        }
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Not a defect", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change status']").Filter(new() { HasTextString = "Not a defect" }));

        // Picking Completed brings up the warning instead of completing. The dialog is only
        // rendered then, so its text isn't sitting hidden on every dashboard.
        await Page.GotoAsync(dashboardUrl);
        var statusMenu = Page.Locator("summary[aria-label='Change project status']");
        var dialog = Page.Locator("#complete-project-modal");
        await Expect(dialog).ToHaveCountAsync(0);
        await statusMenu.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Completed", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?confirmComplete=true$"));
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.Locator("[data-outstanding-tests]")).ToContainTextAsync("1 of 1 test case hasn't passed");
        await Expect(dialog.Locator("[data-outstanding-tests]")).ToContainTextAsync("1 failed");
        await Expect(dialog.Locator("[data-outstanding-defects]")).ToContainTextAsync("1 defect hasn't been closed off");
        await Expect(dialog.GetByRole(AriaRole.Link, new() { Name = openDefect })).ToBeVisibleAsync();
        await Expect(dialog.GetByText(dismissedDefect)).ToHaveCountAsync(0);

        // Cancel leaves the project as it was.
        await dialog.GetByRole(AriaRole.Link, new() { Name = "Cancel" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}$"));
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(statusMenu).ToContainTextAsync("Active");

        // Confirming completes it anyway.
        await statusMenu.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Completed", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Mark as completed anyway" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}$"));
        await Expect(statusMenu).ToContainTextAsync("Completed");
    }

    private async Task SetResultAsync(string caseUrl, string result)
    {
        await Page.GotoAsync(caseUrl);
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change result']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = result, Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change result']").Filter(new() { HasTextString = result }));
    }

    [Test]
    public async Task Links_can_be_quick_added_from_the_dashboard()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateProjectAsync($"Quick links {suffix}");
        var dialog = Page.Locator("#add-link-modal");
        var addButton = Page.Locator("button[data-dialog-open='add-link-modal']");

        // No links yet: the button spells out what it does.
        await Expect(addButton).ToHaveTextAsync("Add link");

        // A URL the app doesn't accept comes back with the error, the dialog reopened and
        // what was typed still in it.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByLabel("Link label").FillAsync("Docs");
        await dialog.GetByLabel("Link URL").FillAsync("ftp://example.test/docs");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToContainTextAsync("is not a valid http(s) or mailto URL");
        await Expect(dialog.GetByLabel("Link label")).ToHaveValueAsync("Docs");
        await Expect(dialog.GetByLabel("Link URL")).ToHaveValueAsync("ftp://example.test/docs");

        // Fixed, it's added to the link bar; a second one goes after it.
        await dialog.GetByLabel("Link URL").FillAsync("https://example.test/docs");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Docs" })).ToHaveAttributeAsync("href", "https://example.test/docs");

        // With a link beside it, it's just the "+" (still named "Add link").
        await Expect(addButton).ToHaveTextAsync("");
        await Expect(addButton).ToHaveAttributeAsync("aria-label", "Add link");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync();
        await dialog.GetByLabel("Link label").FillAsync("Staging");
        await dialog.GetByLabel("Link URL").FillAsync("https://staging.example.test");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync();
        await Expect(Page.Locator(".link-group-open")).ToHaveTextAsync(["Docs", "Staging"]);

        // The button stays at the end of the link bar, straight after the newest link.
        await Expect(Page.Locator(".link-group:has-text('Staging') + button[data-dialog-open='add-link-modal']"))
            .ToHaveCountAsync(1);
    }

    [Test]
    public async Task Project_list_can_be_searched()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var keep = $"Alpha search {tag}";
        var hide = $"Beta search {tag}";
        await CreateProjectAsync(keep);
        await CreateProjectAsync(hide);

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.GetByPlaceholder("Search projects").FillAsync($"Alpha search {tag}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"[?&]q=Alpha"));
        await Expect(Page.GetByRole(AriaRole.Button).Filter(new() { HasTextString = keep })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button).Filter(new() { HasTextString = hide })).Not.ToBeVisibleAsync();

        // Clear drops the query and shows the full (paged) list again.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Clear" }).ClickAsync();
        await Expect(Page).Not.ToHaveURLAsync(new Regex(@"[?&]q="));
        await Page.GetByPlaceholder("Search projects").FillAsync($"Beta search {tag}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button).Filter(new() { HasTextString = hide })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Qa_can_assign_a_team_member_from_the_dashboard_and_see_them_grouped_by_role()
    {
        var name = $"E2E team project {Guid.NewGuid():N}";
        var tag = Guid.NewGuid().ToString("N")[..8];
        var memberName = $"Dana Tester {tag}";

        await CreateUserAsync($"e2e-team-qa-{tag}@test.local", memberName, "QA");

        var dashboardUrl = await CreateProjectAsync(name);
        await Page.GotoAsync(dashboardUrl);

        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync(),
            Page.GetByRole(AriaRole.Heading, new() { Name = "Add team members" }));
        await Page.Locator("dialog[open]").Locator("label", new() { HasTextString = memberName })
            .GetByRole(AriaRole.Checkbox).CheckAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/projects/[0-9a-fA-F-]{36}$"));
        var memberRow = Page.Locator("li").Filter(new() { HasTextString = memberName });
        await Expect(memberRow).ToBeVisibleAsync();

        // Removing them empties the team again.
        await memberRow.GetByRole(AriaRole.Button, new() { Name = $"Remove {memberName}" }).ClickAsync();
        await Expect(Page.GetByText("No team members assigned yet.")).ToBeVisibleAsync();
    }
}
