using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>Previous / Next through your assigned defects that still need work.</summary>
[TestFixture]
public class DefectNavigationTests : E2ETestBase
{
    [Test]
    public async Task Steps_through_assigned_defects_most_severe_first()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string critical = $"Critical one {suffix}", medium = $"Medium one {suffix}", low = $"Low one {suffix}",
            unassigned = $"Nobody's {suffix}";
        var dashboardUrl = await CreateProjectAsync($"E2E defect queue {suffix}");
        var me = await CurrentUserNameAsync();
        await AddSelfToTeamAsync(dashboardUrl);

        var lowUrl = await RaiseAsync(dashboardUrl, low, "Low", $"{me} (QA)");
        var unassignedUrl = await RaiseAsync(dashboardUrl, unassigned, "High", null);
        var criticalUrl = await RaiseAsync(dashboardUrl, critical, "Critical", $"{me} (QA)");
        var mediumUrl = await RaiseAsync(dashboardUrl, medium, "Medium", $"{me} (QA)");
        var queue = Page.Locator("[data-assigned-queue]");
        var previous = Page.GetByRole(AriaRole.Navigation, new() { Name = "Assigned defect navigation" }).GetByText("← Previous");
        var next = Page.GetByRole(AriaRole.Navigation, new() { Name = "Assigned defect navigation" }).GetByText("Next →");

        // Most severe first: Critical → Medium → Low (the unassigned High one isn't in the queue).
        await Page.GotoAsync(criticalUrl);
        await Expect(queue).ToHaveTextAsync("1 of 3 assigned to you · 3 to fix");
        await Expect(previous).ToBeDisabledAsync();
        await next.ClickAsync();
        await Expect(Page).ToHaveURLAsync(mediumUrl);
        await Expect(queue).ToHaveTextAsync("2 of 3 assigned to you · 3 to fix");
        await next.ClickAsync();
        await Expect(Page).ToHaveURLAsync(lowUrl);
        await Expect(next).ToBeDisabledAsync();
        await previous.ClickAsync();
        await Expect(Page).ToHaveURLAsync(mediumUrl);
        // Enhanced navigation updates the URL before it patches the page: wait for the defect itself
        // before using its status menu, or the patch can close the menu mid-click.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = medium })).ToBeVisibleAsync();

        // Moving one to To check keeps it queued and shows in the counts.
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "To check", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change status']").Filter(new() { HasTextString = "To check" }));
        await Expect(queue).ToHaveTextAsync("2 of 3 assigned to you · 2 to fix · 1 to check");

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "defect-queue.png") });
        }

        // From a defect outside the queue, it steps to the nearest queued ones either side.
        await Page.GotoAsync(unassignedUrl);
        await Expect(queue).ToHaveTextAsync("3 assigned to you · 2 to fix · 1 to check");
        await previous.ClickAsync();
        await Expect(Page).ToHaveURLAsync(criticalUrl);
    }

    private async Task<string> RaiseAsync(string dashboardUrl, string summary, string severity, string? assignee)
    {
        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await Page.GetByLabel("Summary").FillAsync(summary);
        await PickFormSelectAsync("Severity", severity);
        if (assignee is not null)
        {
            await PickFormSelectAsync("Assigned to", assignee);
        }
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = summary })).ToBeVisibleAsync();
        return Page.Url;
    }
}
