using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for self-service settings (email/password + permissions).</summary>
[TestFixture]
public class SettingsTests : E2ETestBase
{
    [Test]
    public async Task Settings_page_shows_email_and_permissions()
    {
        var email = Environment.GetEnvironmentVariable("QATRACKER_E2E_EMAIL")!;

        await Page.GotoAsync($"{BaseUrl}/settings");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Account" })).ToBeVisibleAsync();
        var accountCard = Page.Locator(".card").Filter(new() { HasText = "Permissions" });
        await Expect(accountCard.GetByText(email)).ToBeVisibleAsync();
        await Expect(accountCard.GetByText("QA", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(accountCard.GetByText("Last sign-in")).ToBeVisibleAsync();
    }

    [Test]
    public async Task User_can_change_their_own_email_and_password()
    {
        var originalEmail = $"e2e-settings-{Guid.NewGuid():N}@test.local";
        var newEmail = $"e2e-settings-{Guid.NewGuid():N}@test.local";
        const string originalPassword = "Str0ng!Passw0rd";
        const string newPassword = "N3wStr0ng!Passw0rd";

        // Create a throwaway user (as the signed-in QA fixture account) to change email/password on,
        // so this test never touches the shared fixture account's own credentials.
        await CreateUserAsync(originalEmail, "E2E Settings User", "QA", originalPassword);

        await SignInAsAsync(originalEmail, originalPassword);

        // Change email.
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.GetByLabel("New email").FillAsync(newEmail);
        await Page.GetByLabel("Current password").Nth(0).FillAsync(originalPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Update email" }).ClickAsync();
        await Expect(Page.GetByText("Email updated.")).ToBeVisibleAsync();
        var accountCard = Page.Locator(".card").Filter(new() { HasText = "Permissions" });
        await Expect(accountCard.GetByText(newEmail)).ToBeVisibleAsync();

        // Change password (now signed in under the new email's session, still same account).
        await Page.GetByLabel("Current password").Nth(1).FillAsync(originalPassword);
        await Page.GetByLabel("New password", new() { Exact = true }).FillAsync(newPassword);
        await Page.GetByLabel("Confirm new password").FillAsync(newPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Update password" }).ClickAsync();
        await Expect(Page.GetByText("Password updated.")).ToBeVisibleAsync();

        // Sign out and confirm the new email + new password actually work.
        await SignOutAsync();
        await SignInAsAsync(newEmail, newPassword);
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/Account/Login"));

        // Clean up: sign back in as the fixture QA account and delete the throwaway user.
        await SignOutAsync();
        await SignIn();
        await Page.GotoAsync($"{BaseUrl}/admin/users?q={newEmail}");
        var row = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = newEmail });
        await row.GetByRole(AriaRole.Link).ClickAsync();
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Delete user" }).ClickAsync(),
            Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
    }

    [Test]
    public async Task User_can_change_notification_sound_preference()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");

        var notifCard = Page.Locator(".card").Filter(new() { HasText = "Notifications" });
        await Expect(notifCard.GetByRole(AriaRole.Button, new() { Name = "On", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true"); // enabled by default
        await Expect(notifCard.GetByRole(AriaRole.Button, new() { Name = "Success" })).ToHaveAttributeAsync("aria-pressed", "true"); // default sound

        // Preview doesn't submit the form or change the selection.
        await notifCard.Locator("[data-play-sound='synth']").ClickAsync();
        await Expect(notifCard.GetByRole(AriaRole.Button, new() { Name = "Success" })).ToHaveAttributeAsync("aria-pressed", "true");

        // Clicking a sound applies it immediately — no Save step.
        await notifCard.GetByRole(AriaRole.Button, new() { Name = "Synth" }).ClickAsync();
        await Expect(notifCard.GetByRole(AriaRole.Button, new() { Name = "Synth" })).ToHaveAttributeAsync("aria-pressed", "true");

        await Page.ReloadAsync();
        notifCard = Page.Locator(".card").Filter(new() { HasText = "Notifications" });
        await Expect(notifCard.GetByRole(AriaRole.Button, new() { Name = "Synth" })).ToHaveAttributeAsync("aria-pressed", "true");

        // Turning the toggle off hides the sound picker entirely.
        await notifCard.GetByRole(AriaRole.Button, new() { Name = "Off", Exact = true }).ClickAsync();
        notifCard = Page.Locator(".card").Filter(new() { HasText = "Notifications" });
        await Expect(notifCard.GetByRole(AriaRole.Button, new() { Name = "Off", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(notifCard.Locator("[data-play-sound]").First).Not.ToBeVisibleAsync();

        // Restore defaults so this doesn't leak into other tests using the same fixture account.
        await notifCard.GetByRole(AriaRole.Button, new() { Name = "On", Exact = true }).ClickAsync();
        notifCard = Page.Locator(".card").Filter(new() { HasText = "Notifications" });
        await Expect(notifCard.GetByRole(AriaRole.Button, new() { Name = "On", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true");
        await notifCard.GetByRole(AriaRole.Button, new() { Name = "Success" }).ClickAsync();
        await Expect(notifCard.GetByRole(AriaRole.Button, new() { Name = "Success" })).ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Test]
    public async Task Dark_mode_survives_an_enhanced_navigation()
    {
        var html = Page.Locator("html");

        try
        {
            // The Appearance buttons apply immediately — no Save step.
            await Page.GotoAsync($"{BaseUrl}/settings");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Dark", Exact = true }).ClickAsync();
            await Expect(html).ToHaveClassAsync(new Regex(@"\bdark\b"));

            // Navigating via a sidebar link is an enhanced navigation — it must not reset the theme.
            await Page.GetByRole(AriaRole.Link, new() { Name = "All projects" }).First.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex("/projects$"));
            await Expect(html).ToHaveClassAsync(new Regex(@"\bdark\b"));
        }
        finally
        {
            await Page.GotoAsync($"{BaseUrl}/settings");
            await Page.GetByRole(AriaRole.Button, new() { Name = "System", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "System", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true");
        }
    }

    private async Task SignInAsAsync(string email, string password)
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByLabel("Password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/Account/Login"));
    }

    private async Task SignOutAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.Locator("header details summary").Last.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Account/Login"));
    }
}
