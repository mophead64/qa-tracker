using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for system settings (stats/status + user management).</summary>
[TestFixture]
public class AdminTests : E2ETestBase
{
    [Test]
    public async Task Qa_sees_system_settings_link_and_stats_and_status()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.GetByRole(AriaRole.Link, new() { Name = "System settings" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "System settings" })).ToBeVisibleAsync();

        var statsGrid = Page.Locator("div.grid").First;
        foreach (var label in new[] { "Projects", "Test cases", "Defects", "Files" })
        {
            await Expect(statsGrid.GetByText(label, new() { Exact = true })).ToBeVisibleAsync();
        }
        // The Files card also reports how much storage the attachments use.
        await Expect(statsGrid.GetByText(new Regex(@"\d+(\.\d+)? (B|KB|MB|GB) stored"))).ToBeVisibleAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "System status" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Authentication")).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex("Local accounts"))).ToBeVisibleAsync();
        await Expect(Page.GetByText("Telemetry")).ToBeVisibleAsync();

        // Uploads card: the per-file limit is editable here.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Uploads" })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Maximum file size (MB)")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Qa_can_create_edit_and_delete_a_user()
    {
        var email = $"e2e-{Guid.NewGuid():N}@test.local";

        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Page.GetByRole(AriaRole.Link, new() { Name = "New user" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users/new$"));

        // Fill and submit inside the same retried callback: input typed before the
        // interactive circuit attaches gets wiped out by the first real render, so a
        // fill-once-then-just-click-repeatedly approach can retry forever on an empty form.
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Email").FillAsync(email);
                await Page.GetByLabel("Full name").FillAsync("E2E Test User");
                await Page.GetByLabel("Password", new() { Exact = true }).FillAsync("Str0ng!Passw0rd");
                if (!await Page.GetByRole(AriaRole.Checkbox).Nth(1).IsCheckedAsync())
                {
                    await Page.GetByText("Dev", new() { Exact = true }).ClickAsync();
                }

                await Page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();
            },
            Page.GetByText(email));

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        var row = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = email });
        await Expect(row.GetByText("E2E Test User")).ToBeVisibleAsync();
        await Expect(row.GetByText("Dev")).ToBeVisibleAsync();

        await row.GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users/[^/]+/edit$"));

        // Probe on the renamed text, not just the list page loading: "Full name" is an
        // optional field, so a submit with it still empty (an early attempt racing the
        // interactive circuit's attach) would "succeed" too, just without the rename.
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Full name").FillAsync("E2E Renamed User");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            },
            Page.GetByText("E2E Renamed User"));

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        var renamedRow = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = email });
        await Expect(renamedRow.GetByText("E2E Renamed User")).ToBeVisibleAsync();

        await renamedRow.GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users/[^/]+/edit$"));

        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Delete user" }).ClickAsync(),
            Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        await Expect(Page.GetByText(email)).Not.ToBeVisibleAsync();
    }
}
