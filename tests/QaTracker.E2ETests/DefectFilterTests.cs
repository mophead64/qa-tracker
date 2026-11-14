using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The per-user status filter on the defects page.</summary>
[TestFixture]
public class DefectFilterTests : E2ETestBase
{
    [Test]
    public async Task Filter_narrows_the_defects_and_follows_the_user_across_projects()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string notFixed = $"Still broken {suffix}", fixing = $"Being fixed {suffix}", notADefect = $"Works as designed {suffix}",
            elsewhere = $"Elsewhere {suffix}";
        var viewerEmail = $"e2e-defect-filter-{suffix}@test.local";

        // Project 1: one Not fixed, one Fixing, one Not a defect.
        var dashboardUrl = await CreateProjectAsync($"E2E defect filter {suffix}");
        await RaiseDefectAsync(dashboardUrl, notFixed);
        await RaiseDefectAsync(dashboardUrl, fixing, status: "Fixing");
        await RaiseDefectAsync(dashboardUrl, notADefect, status: "Not a defect");

        // Project 2: a single Not fixed defect.
        var otherDashboardUrl = await CreateProjectAsync($"E2E defect filter other {suffix}");
        await RaiseDefectAsync(otherDashboardUrl, elsewhere);

        // A dedicated viewer, so their saved filter can't change any other test's view.
        await CreateUserAsync(viewerEmail, $"Defect filter viewer {suffix}", "Dev", DevPassword);
        await using var context = await Browser.NewContextAsync();
        var page = await SignInAsync(context, viewerEmail);
        var toggle = page.Locator("summary[aria-label='Filter defects']");
        ILocator Defect(string summary) => page.GetByRole(AriaRole.Link, new() { Name = summary, Exact = true });

        await page.GotoAsync($"{dashboardUrl}/defects");
        await Expect(toggle).ToContainTextAsync("All defects");
        foreach (var name in new[] { notFixed, fixing, notADefect })
        {
            await Expect(Defect(name)).ToBeVisibleAsync();
        }

        // Open: everything not closed; the Closed group drops out.
        await PickAsync(page, "Open");
        await Expect(Defect(notFixed)).ToBeVisibleAsync();
        await Expect(Defect(fixing)).ToBeVisibleAsync();
        await Expect(Defect(notADefect)).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Closed defects" })).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-filter-summary]")).ToHaveTextAsync("Showing 2 of 3 defects");

        // Every option fits — the open menu doesn't scroll.
        await toggle.ClickAsync();
        var panel = page.Locator("details[data-dropdown][open] > div");
        Assert.That(await panel.EvaluateAsync<bool>("el => el.scrollHeight <= el.clientHeight"), Is.True,
            "the filter menu should show every option without scrolling");
        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, "defect-filter.png") });
        }
        await toggle.ClickAsync();

        await PickAsync(page, "Closed");
        await Expect(Defect(notADefect)).ToBeVisibleAsync();
        await Expect(Defect(notFixed)).ToHaveCountAsync(0);

        await PickAsync(page, "Fixing");
        await Expect(Defect(fixing)).ToBeVisibleAsync();
        await Expect(Defect(notFixed)).ToHaveCountAsync(0);

        // The choice is the user's: project 2 opens filtered to Fixing, has none, and offers a way back.
        await page.GotoAsync($"{otherDashboardUrl}/defects");
        await Expect(toggle).ToContainTextAsync("Fixing");
        await Expect(page.GetByText("No defects match “Fixing”.")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Show all defects" }).ClickAsync();
        await Expect(Defect(elsewhere)).ToBeVisibleAsync();
        await Expect(toggle).ToContainTextAsync("All defects");

        await DeleteUserAsync(viewerEmail);
    }


    private async Task PickAsync(IPage page, string option)
    {
        await page.Locator("summary[aria-label='Filter defects']").ClickAsync();
        // Match the option's own label exactly — a grouped option's description can mention
        // another option ("Not fixed, fixing or to check").
        await page.Locator("details[data-dropdown][open] button")
            .Filter(new() { Has = page.GetByText(option, new() { Exact = true }) }).ClickAsync();
        await Expect(page.Locator("summary[aria-label='Filter defects']")).ToContainTextAsync(option);
    }
}
