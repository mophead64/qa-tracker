using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for notifications (bell badge + clear all).</summary>
[TestFixture]
public class NotificationsTests : E2ETestBase
{
    [Test]
    public async Task Dev_team_member_is_notified_when_a_new_defect_is_raised_and_can_dismiss_it()
    {
        var devEmail = $"e2e-notif-dev-{Guid.NewGuid():N}@test.local";
        const string devPassword = "Str0ng!Passw0rd";
        var projectName = $"E2E notif project {Guid.NewGuid():N}";

        // Create a throwaway Dev user (as the fixture QA account).
        await CreateUserAsync(devEmail, "E2E Notif Dev", "Dev", devPassword);

        // Create a project and add the Dev to its team from the dashboard's Team card.
        var dashboardUrl = await CreateProjectAsync(projectName);
        await Page.GotoAsync(dashboardUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync(),
            Page.GetByRole(AriaRole.Heading, new() { Name = "Add team members" }));
        await Page.Locator("dialog[open]")
            .Locator("label", new() { HasTextString = "E2E Notif Dev" })
            .GetByRole(AriaRole.Checkbox).CheckAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("E2E Notif Dev")).ToBeVisibleAsync();

        // Sign in as the Dev in a separate browser context and confirm no notifications yet.
        await using var devContext = await Browser.NewContextAsync();
        var devPage = await devContext.NewPageAsync();
        await devPage.GotoAsync($"{BaseUrl}/Account/Login");
        await devPage.GetByLabel("Email").FillAsync(devEmail);
        await devPage.GetByLabel("Password").FillAsync(devPassword);
        await devPage.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(devPage).Not.ToHaveURLAsync(new Regex("/Account/Login"));
        var devBellSummary = devPage.Locator("summary[aria-label='Notifications']");
        await devBellSummary.ClickAsync();
        await Expect(devPage.GetByText("Nothing new.")).ToBeVisibleAsync();

        // Back as QA: raise a defect on the project.
        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync("Login button does nothing");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = "Login button does nothing" }));

        // The Dev should now see one notification about it.
        await devPage.GotoAsync($"{BaseUrl}/");
        var bellSummary = devPage.Locator("summary[aria-label='Notifications']");
        await Expect(bellSummary.GetByText("1")).ToBeVisibleAsync();
        await bellSummary.ClickAsync();
        var notification = devPage.GetByText("New defect D-1: Login button does nothing");
        await Expect(notification).ToBeVisibleAsync();

        // "Clear all" empties the dropdown and clears the badge.
        await devPage.GetByRole(AriaRole.Button, new() { Name = "Clear all" }).ClickAsync();
        await Expect(notification).Not.ToBeVisibleAsync();
        await Expect(bellSummary.GetByText("1")).Not.ToBeVisibleAsync();
        await bellSummary.ClickAsync();
        await Expect(devPage.GetByText("Nothing new.")).ToBeVisibleAsync();

        // Clean up the throwaway Dev user.
        await Page.GotoAsync($"{BaseUrl}/admin/users?q={devEmail}");
        var row = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = devEmail });
        await row.GetByRole(AriaRole.Link).ClickAsync();
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Delete user" }).ClickAsync(),
            Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }));
        await SubmitUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync(),
            Page.GetByRole(AriaRole.Link, new() { Name = "New user" }));
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        await Expect(Page.GetByText(devEmail)).Not.ToBeVisibleAsync();
    }
}
