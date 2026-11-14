using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>The read-only Viewer role: sees projects, test cases and defects, comments and reacts,
/// can always be @-mentioned, and can change nothing else — no edits, results, statuses, uploads,
/// team places or settings.</summary>
[TestFixture]
public class ViewerTests : E2ETestBase
{
    [Test]
    public async Task Viewer_comments_and_reacts_but_changes_nothing_else()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var viewerName = $"Viewer {suffix}";
        var viewerEmail = $"e2e-viewer-{suffix}@test.local";
        var scenario = $"Viewer case {suffix}";
        var summary = $"Viewer defect {suffix}";
        var adminNote = $"Admin note {suffix}";

        await CreateUserAsync(viewerEmail, viewerName, "Viewer");
        var dashboardUrl = await CreateProjectAsync($"E2E viewer {suffix}");
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Viewer scope {suffix}", scenario))[0];
        var defectUrl = await RaiseDefectAsync(dashboardUrl, summary);

        // Viewers aren't on the team but can still be @-mentioned: picked from the list and notified.
        var box = Page.Locator("#new-comment");
        await box.PressSequentiallyAsync($"{adminNote} @");
        await box.PressSequentiallyAsync(viewerName);
        await box.PressAsync("Tab");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
        var noteItem = Page.Locator("#comments li").Filter(new() { HasTextString = adminNote });
        await Expect(noteItem.Locator("p span").Filter(new() { HasTextString = "@" + viewerName })).ToHaveClassAsync(new Regex("bg-brand-100"));

        // Viewers can't be put on a team.
        await Page.GotoAsync(dashboardUrl);
        var storageConfigured = await Page.Locator("[data-upload-open]").CountAsync() > 0;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Add team members" })).ToBeVisibleAsync();
        await Expect(Page.Locator("dialog[open] label").Filter(new() { HasTextString = viewerName })).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");

        try
        {
            await using var context = await Browser.NewContextAsync();
            var viewer = await SignInAsync(context, viewerEmail);
            var main = viewer.GetByRole(AriaRole.Main);

            await viewer.Locator("summary[aria-label='Notifications']").ClickAsync();
            await Expect(viewer.GetByText("mentioned you in a comment on D-1: " + summary)).ToHaveCountAsync(1);

            // Dashboard: readable, no edit / status / team controls, but comments work.
            await viewer.GotoAsync(dashboardUrl);
            await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true })).ToHaveCountAsync(0);
            await Expect(viewer.Locator("summary[aria-label='Change project status']")).ToHaveCountAsync(0);
            await Expect(viewer.Locator("#team").GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true })).ToHaveCountAsync(0);
            await Expect(viewer.Locator("[data-upload-open]")).ToHaveCountAsync(0);
            if (storageConfigured)
            {
                // …though files can still go with their own comments.
                await Expect(viewer.Locator("#comments label", new() { HasTextString = "Add attachments" })).ToBeVisibleAsync();
            }
            await viewer.Locator("#new-comment").FillAsync($"Dashboard thought {suffix}");
            await viewer.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            await Expect(viewer.Locator("#comments li").Filter(new() { HasTextString = $"Dashboard thought {suffix}" })).ToBeVisibleAsync();

            // Test case: no result buttons, edit or uploads; can comment.
            await viewer.GotoAsync(caseUrl);
            await Expect(viewer.GetByRole(AriaRole.Heading, new() { Name = scenario })).ToBeVisibleAsync();
            await Expect(viewer.GetByRole(AriaRole.Button, new() { Name = "Mark as passed" })).ToHaveCountAsync(0);
            await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true })).ToHaveCountAsync(0);
            await Expect(viewer.Locator("[data-upload-open]")).ToHaveCountAsync(0);
            await viewer.Locator("#new-comment").FillAsync($"Case question {suffix}");
            await viewer.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
            await Expect(viewer.Locator("#comments li").Filter(new() { HasTextString = $"Case question {suffix}" })).ToBeVisibleAsync();

            // Defect: no pickup, status, edit, queue or uploads; can react to a comment.
            await viewer.GotoAsync(defectUrl);
            await Expect(viewer.GetByRole(AriaRole.Heading, new() { Name = summary })).ToBeVisibleAsync();
            await Expect(viewer.GetByRole(AriaRole.Button, new() { Name = "Assign to me" })).ToHaveCountAsync(0);
            await Expect(viewer.Locator("summary[aria-label='Change status']")).ToHaveCountAsync(0);
            await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true })).ToHaveCountAsync(0);
            await Expect(viewer.Locator("nav[aria-label='Assigned defect navigation']")).ToHaveCountAsync(0);
            await Expect(viewer.Locator("[data-upload-open]")).ToHaveCountAsync(0);
            var thumbsUp = viewer.Locator("#comments li").Filter(new() { HasTextString = adminNote }).Locator("button[data-reaction='Up']");
            await thumbsUp.ClickAsync();
            await Expect(viewer.Locator("#comments li").Filter(new() { HasTextString = adminNote }).Locator("button[data-reaction='Up']"))
                .ToHaveAttributeAsync("aria-pressed", "true");

            // Posting to a contributor action directly is refused.
            var landed = await viewer.EvaluateAsync<string>(
                "async url => (await fetch(url, { method: 'POST', body: new URLSearchParams({ assign: 'true' }) })).url",
                defectUrl + "/assignee/self");
            Assert.That(landed, Does.Contain("AccessDenied"));

            // No System settings; personal Settings keep just the notifications that can reach them.
            await viewer.GotoAsync($"{BaseUrl}/admin");
            await Expect(viewer.GetByRole(AriaRole.Heading, new() { Name = "Access denied" })).ToBeVisibleAsync();
            await viewer.GotoAsync($"{BaseUrl}/settings");
            await Expect(viewer.Locator("[data-notification-settings] input[name='trigger']")).ToHaveCountAsync(2);
        }
        finally
        {
            await DeleteUserAsync(viewerEmail);
        }

        // Still nobody assigned to the defect.
        await Page.GotoAsync(defectUrl);
        await Expect(Page.Locator("summary[aria-label='Change assignee']")).ToContainTextAsync("Unassigned");
    }
}
