using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>New-password fields: the rules checklist and strength meter, blocking a weak password
/// before it posts, generating a strong one, and the one-time sign-in details after creating a user.</summary>
[TestFixture]
public class PasswordFieldTests : E2ETestBase
{
    [Test]
    public async Task Admin_generates_a_password_shares_it_once_and_the_user_changes_it_in_settings()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"e2e-password-{suffix}@test.local";

        await Page.GotoAsync($"{BaseUrl}/admin/users/new");
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByLabel("Full name").FillAsync($"Password User {suffix}");
        await PickFormSelectAsync("Role", "Dev");

        // A weak password: rules stay unticked, the meter says so, and the form won't post.
        var password = Page.GetByLabel("Password", new() { Exact = true });
        var field = Page.Locator("[data-password-field]");
        await password.FillAsync("abc");
        await Expect(field.Locator("[data-rule='length']")).Not.ToHaveAttributeAsync("data-met", "");
        await Expect(field.Locator("[data-rule='lower']")).ToHaveAttributeAsync("data-met", "");
        await Expect(field.Locator("[data-password-strength]")).ToHaveTextAsync("Doesn't meet the rules yet");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users/new$"));
        await Expect(Page.Locator("[data-new-credentials]")).ToHaveCountAsync(0);
        Assert.That(await password.EvaluateAsync<bool>("e => e.validity.valid"), Is.False);

        // Generate: every rule ticked, shown in the clear, rated strong.
        await field.GetByRole(AriaRole.Button, new() { Name = "Generate a strong password" }).ClickAsync();
        await Expect(field.Locator("[data-rule]:not([data-met])")).ToHaveCountAsync(0);
        await Expect(field.Locator("[data-password-strength]")).ToHaveTextAsync("Strong");
        await Expect(password).ToHaveAttributeAsync("type", "text");
        var generated = await password.InputValueAsync();
        Assert.That(generated, Has.Length.GreaterThanOrEqualTo(20));

        // Created: the details are shown this once.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();
        await Expect(Page.Locator("[data-credential='email']")).ToHaveTextAsync(email);
        await Expect(Page.Locator("[data-credential='password']")).ToHaveTextAsync(generated);
        await Expect(Page.Locator("[data-copy-credentials]")).ToHaveAttributeAsync("data-copy-link", $"Email: {email}\nPassword: {generated}");

        try
        {
            // The new user signs in with it, and generates a new password of their own in Settings.
            await using var context = await Browser.NewContextAsync();
            var user = await context.NewPageAsync();
            await user.GotoAsync($"{BaseUrl}/Account/Login");
            await user.GetByLabel("Email").FillAsync(email);
            await user.GetByLabel("Password").FillAsync(generated);
            await user.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
            await Expect(user).Not.ToHaveURLAsync(new Regex("/Account/Login"));

            await user.GotoAsync($"{BaseUrl}/settings");
            await user.GetByRole(AriaRole.Button, new() { Name = "Change password" }).ClickAsync();
            var modal = user.Locator("#change-password-modal");
            await modal.GetByLabel("Current password").ClickAsync();
            await modal.GetByLabel("Current password").FillAsync(generated);
            await Expect(modal.Locator("[data-password-rules] li")).ToHaveCountAsync(5);
            await modal.GetByRole(AriaRole.Button, new() { Name = "Generate a strong password" }).ClickAsync();
            var newPassword = await modal.GetByLabel("New password", new() { Exact = true }).InputValueAsync();
            Assert.That(newPassword, Is.Not.EqualTo(generated));
            await Expect(modal.GetByLabel("Confirm new password")).ToHaveValueAsync(newPassword);
            await modal.GetByRole(AriaRole.Button, new() { Name = "Update password" }).ClickAsync();
            await Expect(user.GetByText("Password updated.")).ToBeVisibleAsync();
        }
        finally
        {
            await DeleteUserAsync(email);
        }
    }

    [Test]
    public async Task Settings_blocks_a_confirmation_that_does_not_match()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Change password" }).ClickAsync();
        var modal = Page.Locator("#change-password-modal");
        await modal.GetByLabel("Current password").ClickAsync();
        await modal.GetByLabel("Current password").FillAsync("not-checked");
        await modal.GetByLabel("New password", new() { Exact = true }).ClickAsync();
        await modal.GetByLabel("New password", new() { Exact = true }).FillAsync("Str0ng!Passw0rd-x");
        await modal.GetByLabel("Confirm new password").ClickAsync();
        await modal.GetByLabel("Confirm new password").FillAsync("Str0ng!Passw0rd-y");

        await modal.GetByRole(AriaRole.Button, new() { Name = "Update password" }).ClickAsync();

        // Caught in the browser: no post, so no server error and the dialog stays open.
        Assert.That(await modal.GetByLabel("Confirm new password").EvaluateAsync<bool>("e => e.validity.valid"), Is.False);
        await Expect(modal).ToBeVisibleAsync();
        await Expect(modal.Locator(".alert-danger")).ToHaveCountAsync(0);
    }
}
