using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>System settings › Users › a user › View activity: stat tiles, the per-project
/// breakdown, scoping to one project, and the drill-down views.</summary>
[TestFixture]
public class UserActivityTests : E2ETestBase
{
    [Test]
    public async Task Admin_sees_a_users_comments_and_projects_and_can_drill_down()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var devName = $"Activity Dev {suffix}";
        var devEmail = $"e2e-user-activity-{suffix}@test.local";
        var projectName = $"E2E user activity {suffix}";
        var quietProject = $"E2E user activity quiet {suffix}";
        var note = $"Activity note {suffix}";

        await CreateUserAsync(devEmail, devName, "Dev", DevPassword);
        var dashboardUrl = await CreateProjectAsync(projectName);
        await AddToTeamAsync(dashboardUrl, devName);
        var quietUrl = await CreateProjectAsync(quietProject);
        await AddToTeamAsync(quietUrl, devName);

        await using (var devContext = await Browser.NewContextAsync())
        {
            var devPage = await SignInAsync(devContext, devEmail);
            await devPage.GotoAsync(dashboardUrl);
            await devPage.Locator("#new-comment").FillAsync(note);
            await devPage.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            await Expect(devPage.Locator("#comments li").Filter(new() { HasTextString = note })).ToBeVisibleAsync();
        }

        try
        {
            // Users › the row's View activity button (the edit page has one too).
            await Page.GotoAsync($"{BaseUrl}/admin/users?q={devEmail}");
            var row = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = devEmail });
            await row.Locator("a[data-user-edit]").ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "View activity" })).ToBeVisibleAsync();
            await Page.GoBackAsync();
            await row.GetByRole(AriaRole.Link, new() { Name = "View activity" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex("/admin/users/[^/]+/activity$"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = devName, Level = 1 })).ToBeVisibleAsync();

            var stats = Page.Locator("[data-activity-stats]");
            await Expect(stats.Locator("[data-stat='Comments']")).ToContainTextAsync("1");
            await Expect(stats.Locator("[data-stat='Results recorded']")).ToContainTextAsync("0");

            // Both team projects are listed; only one has activity.
            var projects = Page.Locator("[data-activity-projects] li");
            var active = projects.Filter(new() { HasTextString = projectName });
            await Expect(active).ToContainTextAsync("1 comment");
            await Expect(active).ToContainTextAsync("On the team");
            await Expect(projects.Filter(new() { HasTextString = quietProject })).ToContainTextAsync("No activity yet");

            var comments = Page.Locator("[data-activity-panel='comments']");
            await Expect(comments.Locator("[data-comment-snippet]")).ToHaveTextAsync(note);

            // Scope to the quiet project: nothing there.
            await Page.Locator("[data-activity-scope] > summary").ClickAsync();
            await Page.Locator("[data-activity-scope] a", new() { HasTextString = quietProject }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"\?project="));
            await Expect(Page.Locator("[data-activity-projects]")).ToHaveCountAsync(0);
            await Expect(Page.Locator("[data-activity-stats] [data-stat='Comments']")).ToContainTextAsync("0");
            await Expect(Page.Locator("[data-activity-panel='comments']")).ToContainTextAsync("No comments posted.");

            // Drill down into the timeline across every project.
            await Page.Locator("[data-activity-scope] > summary").ClickAsync();
            await Page.Locator("[data-activity-scope] a", new() { HasTextString = "All projects" }).ClickAsync();
            await Page.Locator("[data-activity-view='timeline']").ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"view=timeline"));
            var entry = Page.Locator("[data-activity-panel='timeline'] [data-history] > li");
            await Expect(entry).ToHaveCountAsync(1);
            await Expect(entry).ToContainTextAsync($"{devName} commented on {projectName} (project dashboard)");

            // The entry goes straight to the comment.
            await entry.GetByRole(AriaRole.Link).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"commentsPage=1#comment-"));
            await Expect(Page.Locator("#comments li").Filter(new() { HasTextString = note })).ToBeVisibleAsync();
        }
        finally
        {
            await DeleteUserAsync(devEmail);
        }
    }
}
