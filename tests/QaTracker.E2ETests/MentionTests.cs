using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for @-mentions in defect and test-case comments: the picker,
/// the green tag while typing and once posted, no duplicate tags, and the notification.</summary>
[TestFixture]
public class MentionTests : E2ETestBase
{
    private const string DevPassword = "Str0ng!Passw0rd";

    private ILocator MentionList => Page.Locator("[data-mention-list]");

    private ILocator Options => Page.Locator("[data-mention-option]");

    private ILocator Box => Page.Locator("#new-comment");

    [Test]
    public async Task Mentioning_in_a_defect_comment_tags_the_person_once_and_notifies_them()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var devName = $"Mentionee {suffix}";
        var devEmail = $"e2e-mention-{suffix}@test.local";
        var summary = $"Mention defect {suffix}";
        var tag = $"@{devName}";

        await CreateUserAsync(devEmail, devName, "Dev", DevPassword);
        var dashboardUrl = await CreateProjectAsync($"E2E mention project {suffix}");
        await AddSelfToTeamAsync(dashboardUrl);
        await AddToTeamAsync(dashboardUrl, devName);

        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));

        // "@" opens the picker with the team — but never the person typing.
        await Box.ClickAsync();
        await Box.PressSequentiallyAsync("Hi @");
        await Expect(MentionList).ToBeVisibleAsync();
        await Expect(Options).ToHaveCountAsync(1);
        await Expect(Options.First).ToContainTextAsync(devName);

        // Typing narrows it; a name that matches nobody closes it.
        await Box.PressSequentiallyAsync("Mentionee");
        await Expect(Options).ToHaveCountAsync(1);
        await Box.PressSequentiallyAsync("zzz");
        await Expect(MentionList).ToBeHiddenAsync();
        await Box.PressAsync("Backspace");
        await Box.PressAsync("Backspace");
        await Box.PressAsync("Backspace");
        await Expect(MentionList).ToBeVisibleAsync();

        // Enter picks: the tag is written into the text and highlighted while typing.
        await Box.PressAsync("Enter");
        await Expect(MentionList).ToBeHiddenAsync();
        await Expect(Box).ToHaveValueAsync($"Hi {tag} ");
        await Expect(Page.Locator("[data-mention-mirror] mark")).ToHaveTextAsync(tag);
        await Expect(Page.Locator("input[name=mentions]")).ToHaveCountAsync(1);

        // Already tagged, so they're no longer offered...
        await Box.PressSequentiallyAsync("and @Mentionee");
        await Expect(MentionList).ToBeHiddenAsync();

        // ...until the tag is deleted from the text — then the highlight goes and they're back.
        await Box.FillAsync("");
        await Expect(Page.Locator("[data-mention-mirror] mark")).ToHaveCountAsync(0);
        await Expect(Page.Locator("input[name=mentions]")).ToHaveCountAsync(0);
        await Box.PressSequentiallyAsync("@Mentionee");
        await Expect(Options).ToHaveCountAsync(1);

        // Post a comment whose tag was picked and then removed: no mention, no notification.
        await Box.FillAsync("");
        await Box.PressSequentiallyAsync("@Mentionee");
        await Box.PressAsync("Enter");
        await Box.FillAsync("Changed my mind");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
        await Expect(Page.GetByText("Changed my mind")).ToBeVisibleAsync();

        // Now a real one, with the tag kept.
        await Box.PressSequentiallyAsync("Please look, ");
        await Box.PressSequentiallyAsync("@Mentionee");
        await Box.PressAsync("Tab");
        await Box.PressSequentiallyAsync("thanks");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();

        // Posted, the tag keeps its green highlight (and only the tag does).
        var posted = Page.Locator("li p span").Filter(new() { HasTextString = tag });
        await Expect(posted).ToHaveCountAsync(1);
        await Expect(posted).ToHaveTextAsync(tag);
        await Expect(posted).ToHaveClassAsync(new Regex("bg-brand-100"));
        await Expect(Page.Locator("li p").Filter(new() { HasTextString = "Please look," }))
            .ToContainTextAsync($"Please look, {tag} thanks");

        // The mentioned Dev has exactly one mention notification, linking to the defect.
        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);
        await devPage.Locator("summary[aria-label='Notifications']").ClickAsync();
        var mentions = devPage.GetByText("mentioned you in a comment on D-1: " + summary);
        await Expect(mentions).ToHaveCountAsync(1);
        await mentions.ClickAsync();
        await Expect(devPage).ToHaveURLAsync(new Regex("/defects/[0-9a-fA-F-]{36}$"));
        await Expect(devPage.GetByRole(AriaRole.Heading, new() { Name = summary })).ToBeVisibleAsync();

        await DeleteUserAsync(devEmail);
    }

    [Test]
    public async Task Mentioning_in_a_test_case_comment_notifies_and_links_to_the_test_case()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var devName = $"Mentionee {suffix}";
        var devEmail = $"e2e-mention-{suffix}@test.local";
        var scenario = $"Mention scenario {suffix}";
        var tag = $"@{devName}";

        await CreateUserAsync(devEmail, devName, "Dev", DevPassword);
        var dashboardUrl = await CreateProjectAsync($"E2E mention tc project {suffix}");
        await AddToTeamAsync(dashboardUrl, devName);

        await Page.GotoAsync($"{dashboardUrl}/test-cases/scopes/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Name").FillAsync($"Scope {suffix}");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create scope" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = $"Scope {suffix}" }));
        await Page.GetByRole(AriaRole.Link, new() { Name = "New test case" }).ClickAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Scenario").FillAsync(scenario);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create test case" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = scenario }));

        await Box.ClickAsync();
        await Box.PressSequentiallyAsync("@Mentionee");
        await Expect(Options).ToHaveCountAsync(1);
        await Options.First.ClickAsync(); // a mouse pick works as well as the keyboard
        await Expect(Box).ToHaveValueAsync($"{tag} ");
        await Expect(Page.Locator("[data-mention-mirror] mark")).ToHaveTextAsync(tag);
        await Box.PressSequentiallyAsync("fails on Safari");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();

        await Expect(Page.Locator("li p span").Filter(new() { HasTextString = tag })).ToHaveCountAsync(1);

        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);
        await devPage.Locator("summary[aria-label='Notifications']").ClickAsync();
        var mention = devPage.GetByText("mentioned you in a comment on a test case: " + scenario);
        await Expect(mention).ToBeVisibleAsync();
        await mention.ClickAsync();
        await Expect(devPage).ToHaveURLAsync(new Regex("/cases/[0-9a-fA-F-]{36}$"));
        await Expect(devPage.GetByRole(AriaRole.Heading, new() { Name = scenario })).ToBeVisibleAsync();

        await DeleteUserAsync(devEmail);
    }

    private async Task AddToTeamAsync(string dashboardUrl, string fullName)
    {
        await Page.GotoAsync(dashboardUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync(),
            Page.GetByRole(AriaRole.Heading, new() { Name = "Add team members" }));
        await Page.Locator("dialog[open]").Locator("label", new() { HasTextString = fullName })
            .GetByRole(AriaRole.Checkbox).CheckAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("li").Filter(new() { HasTextString = fullName })).ToBeVisibleAsync();
    }

    private async Task<IPage> SignInAsync(IBrowserContext context, string email)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/Account/Login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(DevPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(page).Not.ToHaveURLAsync(new Regex("/Account/Login"));
        return page;
    }

    private async Task DeleteUserAsync(string email)
    {
        await Page.GotoAsync($"{BaseUrl}/admin/users?q={email}");
        var row = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasTextString = email });
        await row.GetByRole(AriaRole.Link).ClickAsync();
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Delete user" }).ClickAsync(),
            Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }));
        await SubmitUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync(),
            Page.GetByRole(AriaRole.Link, new() { Name = "New user" }));
    }
}
