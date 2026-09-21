using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>End-to-end coverage for @-mentions in defect and test-case comments: the picker,
/// the green tag while typing and once posted, no duplicate tags, and the notification.</summary>
[TestFixture]
public class MentionTests : E2ETestBase
{
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

    [Test]
    public async Task Picker_keyboard_navigation_escape_and_posted_tags_survive_a_reload()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var alphaName = $"Alpha {suffix}";
        var bravoName = $"Bravo {suffix}";
        var alphaEmail = $"e2e-mention-a-{suffix}@test.local";
        var bravoEmail = $"e2e-mention-b-{suffix}@test.local";
        var summary = $"Mention keys defect {suffix}";

        await CreateUserAsync(alphaEmail, alphaName, "Dev", DevPassword);
        await CreateUserAsync(bravoEmail, bravoName, "Dev", DevPassword);
        var dashboardUrl = await CreateProjectAsync($"E2E mention keys project {suffix}");
        await AddToTeamAsync(dashboardUrl, alphaName);
        await AddToTeamAsync(dashboardUrl, bravoName);

        await Page.GotoAsync($"{dashboardUrl}/defects/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Summary").FillAsync(summary);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = summary }));

        // Both team members are offered, the first highlighted; arrows move (and wrap) the highlight.
        await Box.ClickAsync();
        await Box.PressSequentiallyAsync("Hey @");
        await Expect(Options).ToHaveCountAsync(2);
        await Expect(Options.Nth(0)).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Options.Nth(1)).ToHaveAttributeAsync("aria-selected", "false");
        await Box.PressAsync("ArrowDown");
        await Expect(Options.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Options.Nth(0)).ToHaveAttributeAsync("aria-selected", "false");
        await Box.PressAsync("ArrowDown");
        await Expect(Options.Nth(0)).ToHaveAttributeAsync("aria-selected", "true");
        await Box.PressAsync("ArrowUp");
        await Expect(Options.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");

        // Escape closes the picker without inserting anything.
        await Box.PressAsync("Escape");
        await Expect(MentionList).ToBeHiddenAsync();
        await Expect(Box).ToHaveValueAsync("Hey @");
        await Expect(Page.Locator("input[name=mentions]")).ToHaveCountAsync(0);

        // Typing a letter reopens it; Enter picks the highlighted (second) entry.
        await Box.PressSequentiallyAsync("B");
        await Expect(Options).ToHaveCountAsync(1);
        await Box.PressAsync("Enter");
        await Expect(Box).ToHaveValueAsync($"Hey @{bravoName} ");

        // Text that merely looks like a mention (not a team member) is left plain.
        await Box.PressSequentiallyAsync("and @Nobody");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();

        var tags = Page.Locator("li p span").Filter(new() { HasTextString = "@" });
        await Expect(tags).ToHaveCountAsync(1);
        await Expect(tags).ToHaveTextAsync($"@{bravoName}");

        // The highlight is rendered from the stored text, so it's still there after a reload.
        await Page.ReloadAsync();
        await Expect(tags).ToHaveCountAsync(1);
        await Expect(tags).ToHaveTextAsync($"@{bravoName}");
        await Expect(Page.Locator("li p").Filter(new() { HasTextString = "and @Nobody" })).ToBeVisibleAsync();

        await DeleteUserAsync(alphaEmail);
        await DeleteUserAsync(bravoEmail);
    }

    [Test]
    public async Task Deleting_a_test_case_or_its_scope_removes_the_mention_notifications_about_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var devName = $"Mentionee {suffix}";
        var devEmail = $"e2e-mention-{suffix}@test.local";
        var scenarioA = $"Cascade scenario A {suffix}";
        var scenarioB = $"Cascade scenario B {suffix}";

        await CreateUserAsync(devEmail, devName, "Dev", DevPassword);
        var dashboardUrl = await CreateProjectAsync($"E2E mention cascade project {suffix}");
        await AddToTeamAsync(dashboardUrl, devName);

        await Page.GotoAsync($"{dashboardUrl}/test-cases/scopes/new");
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Name").FillAsync($"Scope {suffix}");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create scope" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = $"Scope {suffix}" }));

        // Two test cases in the scope, each with a comment that mentions the Dev.
        var caseAUrl = await CreateMentionedTestCaseAsync(dashboardUrl, scenarioA, devName);
        await CreateMentionedTestCaseAsync(dashboardUrl, scenarioB, devName);

        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);
        var devMentions = devPage.Locator("summary[aria-label='Notifications'] ~ * li")
            .Filter(new() { HasTextString = "mentioned you in a comment on a test case" });
        await OpenDevBellAsync(devPage, dashboardUrl);
        await Expect(devMentions).ToHaveCountAsync(2);

        // Deleting one test case takes only its own notification with it (and nothing errors).
        await Page.GotoAsync($"{caseAUrl}/edit");
        await Page.GetByText("Delete test case").ClickAsync();
        await SubmitUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync(),
            Page.GetByRole(AriaRole.Heading, new() { Name = $"Scope {suffix}" }));
        await OpenDevBellAsync(devPage, dashboardUrl);
        await Expect(devMentions).ToHaveCountAsync(1);
        await Expect(devMentions).ToContainTextAsync(scenarioB);

        // Deleting the whole scope removes the rest.
        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit scope" }).ClickAsync();
        await Page.GetByText("Delete scope").ClickAsync();
        await SubmitUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync(),
            Page.GetByText("No scopes yet").Or(Page.GetByRole(AriaRole.Link, new() { Name = "New scope" }).First));
        await OpenDevBellAsync(devPage, dashboardUrl);
        await Expect(devMentions).ToHaveCountAsync(0);
        await Expect(devPage.GetByText("Nothing new.")).ToBeVisibleAsync();

        await DeleteUserAsync(devEmail);
    }

    /// <summary>Creates a test case in the project's only scope, has QA comment mentioning
    /// <paramref name="devName"/> on it, and returns the test case's URL.</summary>
    private async Task<string> CreateMentionedTestCaseAsync(string dashboardUrl, string scenario, string devName)
    {
        await Page.GotoAsync($"{dashboardUrl}/test-cases");
        await Page.GetByRole(AriaRole.Link, new() { Name = "New test case" }).First.ClickAsync();
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Scenario").FillAsync(scenario);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create test case" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = scenario }));
        var url = Page.Url;

        await Box.ClickAsync();
        await Box.PressSequentiallyAsync("@Mentionee");
        await Options.First.ClickAsync();
        await Box.PressSequentiallyAsync("please check");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
        await Expect(Page.Locator("li p span").Filter(new() { HasTextString = $"@{devName}" })).ToHaveCountAsync(1);
        return url;
    }

    /// <summary>Loads a page as the Dev and opens the bell, waiting for its list to render
    /// (either notifications or the "Nothing new." message).</summary>
    private async Task OpenDevBellAsync(IPage devPage, string url)
    {
        await devPage.GotoAsync(url);
        await devPage.Locator("summary[aria-label='Notifications']").ClickAsync();
        await Expect(devPage.GetByText("Clear all").Or(devPage.GetByText("Nothing new."))).ToBeVisibleAsync();
    }
}
