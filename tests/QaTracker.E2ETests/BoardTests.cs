using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>
/// The development board: switching it on for a project, stories with tasks to tick off and
/// reorder, moving cards between columns (menu, keyboard buttons and drag), links to test cases
/// and defects, a bug made from a defect, and "#ST-n" references in comments.
/// </summary>
[TestFixture]
public class BoardTests : E2ETestBase
{
    private ILocator Card(string reference) => Page.Locator($"[data-board-card='{reference}']");

    private ILocator Column(string column) => Page.Locator($"[data-board-column='{column}']");

    /// <summary>A new project with its board switched on; returns the dashboard URL.</summary>
    private async Task<string> CreateBoardProjectAsync(string name)
    {
        var dashboardUrl = await CreateProjectAsync(name);
        await Page.GotoAsync($"{dashboardUrl}/edit");
        await Page.Locator("[data-board-enabled]").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}$"));
        return dashboardUrl;
    }

    private async Task<string> CreateItemAsync(string dashboardUrl, string type, string title)
    {
        // One "New item" button on the board; the type is picked in the form.
        await Page.GotoAsync($"{dashboardUrl}/board");
        await Page.Locator("[data-new-item]").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "New item" })).ToBeVisibleAsync();
        await PickFormSelectAsync("Type", type);
        await SubmitUntil(
            async () =>
            {
                await Page.GetByLabel("Title").FillAsync(title);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create item" }).ClickAsync();
            },
            Page.GetByRole(AriaRole.Heading, new() { Name = title }));
        return Page.Url;
    }

    /// <summary>Runs <paramref name="action"/> and waits for the move board.js posts (so a reload sees it saved).</summary>
    private async Task PostedAsync(string path, Func<Task> action)
    {
        var response = await Page.RunAndWaitForResponseAsync(action, r => r.Url.Contains(path) && r.Request.Method == "POST");
        Assert.That(response.Ok, Is.True, $"{path} answered {response.Status}");
    }

    private async Task AddTaskAsync(string title)
    {
        await Page.Locator("#new-task-title").FillAsync(title);
        await Page.Locator("[data-add-task]").ClickAsync();
        await Expect(Page.Locator("[data-task-title]").Filter(new() { HasTextString = title })).ToBeVisibleAsync();
        await Expect(Page.Locator("#new-task-title")).ToHaveValueAsync("");
    }

    [Test]
    public async Task The_board_is_off_until_switched_on_and_then_in_the_side_nav()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E board off {suffix}");
        await Expect(Page.Locator("[data-nav-board]")).ToHaveCountAsync(0);
        await Page.GotoAsync($"{dashboardUrl}/board");
        await Expect(Page.Locator("[data-board-off]")).ToBeVisibleAsync();

        var boardDashboard = await CreateBoardProjectAsync($"E2E board on {suffix}");

        // Board sits second in the side navigation, straight after the dashboard.
        await Expect(Page.Locator("aside[data-side-nav] nav a .nav-label"))
            .ToHaveTextAsync(["All projects", "Dashboard", "Board", "Test cases", "Defects"]);

        // Switching it on is its own entry in the project's activity, not "edited the project's …".
        await Page.GotoAsync($"{boardDashboard}/activity");
        await Expect(Page.GetByText("enabled the development board")).ToBeVisibleAsync();
        await Expect(Page.GetByText("edited the project's development board")).ToHaveCountAsync(0);

        await Page.Locator("[data-nav-board]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/board$"));
        await Expect(Page.Locator("[data-nav-board]")).ToHaveAttributeAsync("aria-current", "page");
        await Expect(Page.Locator("[data-board-column]")).ToHaveCountAsync(4);
        await Expect(Column("New").Locator("[data-drag-empty]")).ToBeVisibleAsync();

        // "New item" makes either kind: a bug picked from the Type menu lands in New as BG-1.
        var bugTitle = $"Checkout button overlaps the footer {suffix}";
        await CreateItemAsync(boardDashboard, "Bug", bugTitle);
        await Expect(Page.Locator("[data-board-type]")).ToHaveTextAsync("Bug");
        await Page.GotoAsync($"{boardDashboard}/board");
        await Expect(Column("New").Locator("[data-board-card='BG-1']")).ToContainTextAsync(bugTitle);
    }

    [Test]
    public async Task A_story_gets_tasks_to_tick_off_and_reorder_and_moves_across_the_board()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateBoardProjectAsync($"E2E board story {suffix}");
        var storyTitle = $"Save a card at checkout {suffix}";
        var storyUrl = await CreateItemAsync(dashboardUrl, "Story", storyTitle);
        await Expect(Page.Locator("summary[aria-label='Move to column']")).ToContainTextAsync("New");

        await AddTaskAsync("Add the cards table");
        await AddTaskAsync("Wire up the API");
        await Expect(Page.Locator("[data-task]")).ToHaveCountAsync(2);
        await Expect(Page.Locator("[data-task]").Nth(0)).ToHaveAttributeAsync("data-task", "TK-1");

        // Tick the first one off.
        await Page.Locator("[data-task='TK-1'] [data-task-toggle]").ClickAsync();
        await Expect(Page.Locator("[data-task='TK-1']")).ToHaveAttributeAsync("data-task-done", "true");
        await Expect(Page.Locator("[data-task-progress]")).ToHaveTextAsync("1 of 2 done");

        // Reorder: TK-2 up to the top (the ↑ button; the drag posts the same).
        await PostedAsync("/task-order", () => Page.Locator("[data-task='TK-2'] [data-drag-move='up']").ClickAsync());
        await Expect(Page.Locator("[data-task]").Nth(0)).ToHaveAttributeAsync("data-task", "TK-2");
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-task]").Nth(0)).ToHaveAttributeAsync("data-task", "TK-2");

        // A task has its own page, with Active / Complete and a way back to its story.
        await Page.Locator("[data-task='TK-1'] [data-task-title]").ClickAsync();
        await Expect(Page.Locator("summary[aria-label='Change state']")).ToContainTextAsync("Complete");
        await Page.Locator("[data-task-parent] a").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = storyTitle })).ToBeVisibleAsync();

        // Column menu on the item page.
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Move to column']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "In progress", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Move to column']").Filter(new() { HasTextString = "In progress" }));

        // On the board: the card sits in In progress with its task count, and its → button moves it on.
        await Page.GotoAsync($"{dashboardUrl}/board");
        var card = Card("ST-1");
        await Expect(Column("InProgress").Locator("[data-board-card='ST-1']")).ToBeVisibleAsync();
        await Expect(card.Locator("[data-board-tasks]")).ToHaveAttributeAsync("data-board-tasks", "1/2");
        await card.HoverAsync();
        await PostedAsync("/move", () => card.Locator("[data-drag-move='right']").ClickAsync());
        await Expect(Column("Testing").Locator("[data-board-card='ST-1']")).ToBeVisibleAsync();
        await Page.ReloadAsync();
        await Expect(Column("Testing").Locator("[data-board-card='ST-1']")).ToBeVisibleAsync();
        await Expect(Column("Testing").Locator("[data-board-count]")).ToHaveTextAsync("1");

        // Dragging a card (mouse) to Closed.
        var box = (await card.BoundingBoxAsync())!;
        var target = (await Column("Closed").BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(box.X + 20, box.Y + 10);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + 40, box.Y + 30, new() { Steps = 4 });
        await Page.Mouse.MoveAsync(target.X + target.Width / 2, target.Y + 80, new() { Steps = 10 });
        await PostedAsync("/move", () => Page.Mouse.UpAsync());
        await Expect(Column("Closed").Locator("[data-board-card='ST-1']")).ToBeVisibleAsync();
        Assert.That(Page.Url, Does.EndWith("/board"), "the drop must not follow the card's link");
        await Page.ReloadAsync();
        await Expect(Column("Closed").Locator("[data-board-card='ST-1']")).ToBeVisibleAsync();

        // The History modal tells the story.
        await Page.GotoAsync(storyUrl);
        await Page.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        var history = Page.Locator("#history-modal [data-history]");
        await Expect(history.Locator("[data-history-kind='Moved']")).ToHaveCountAsync(3);
        // Columns show as their coloured board badges, newest move first: Testing → Closed.
        await Expect(history.Locator("[data-history-kind='Moved']").First.Locator("[data-history-column]"))
            .ToHaveTextAsync(["Testing", "Closed"]);
        await Expect(history.Locator("[data-history-kind='TaskCompleted']")).ToContainTextAsync("Add the cards table");
    }

    /// <summary>Switches a project's board on or off from its edit form.</summary>
    private async Task SetBoardAsync(string dashboardUrl, bool on)
    {
        await Page.GotoAsync($"{dashboardUrl}/edit");
        await Page.Locator("[data-board-enabled]").SetCheckedAsync(on);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}$"));
    }

    [Test]
    public async Task Switching_the_board_off_hides_it_everywhere_and_switching_it_back_on_keeps_its_items()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateBoardProjectAsync($"E2E board toggle {suffix}");
        var storyTitle = $"Kept while off {suffix}";
        var storyUrl = await CreateItemAsync(dashboardUrl, "Story", storyTitle);
        var defectUrl = await RaiseDefectAsync(dashboardUrl, $"Board toggle defect {suffix}");
        await Expect(Page.Locator("[data-create-bug]")).ToBeVisibleAsync();
        await Page.GotoAsync(dashboardUrl);
        await Expect(Page.Locator("[data-board-summary]")).ToBeVisibleAsync();

        // Off: no Board in the side navigation, on the dashboard or on the defect, and its pages say so.
        await SetBoardAsync(dashboardUrl, false);
        await Expect(Page.Locator("[data-nav-board]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-board-summary]")).ToHaveCountAsync(0);
        await Page.GotoAsync($"{dashboardUrl}/board");
        await Expect(Page.Locator("[data-board-off]")).ToBeVisibleAsync();
        await Page.GotoAsync(storyUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Item not found" })).ToBeVisibleAsync();
        await Page.GotoAsync(defectUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Board toggle defect {suffix}" })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-linked-board-items]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-create-bug]")).ToHaveCountAsync(0);

        await Page.GotoAsync($"{dashboardUrl}/activity");
        await Expect(Page.GetByText("disabled the development board")).ToBeVisibleAsync();

        // On again: the story is back where it was.
        await SetBoardAsync(dashboardUrl, true);
        await Expect(Page.Locator("[data-nav-board]")).ToBeVisibleAsync();
        await Page.GotoAsync($"{dashboardUrl}/board");
        await Expect(Column("New").Locator("[data-board-card='ST-1']")).ToContainTextAsync(storyTitle);
    }

    [Test]
    public async Task Assigning_a_story_notifies_the_assignee_with_a_link_to_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var devName = $"Board Dev {suffix}";
        var devEmail = $"e2e-board-dev-{suffix}@test.local";
        await CreateUserAsync(devEmail, devName, "Dev", DevPassword);
        var dashboardUrl = await CreateBoardProjectAsync($"E2E board assign {suffix}");
        await AddToTeamAsync(dashboardUrl, devName);
        var storyTitle = $"Assigned story {suffix}";
        var storyUrl = await CreateItemAsync(dashboardUrl, "Story", storyTitle);

        var assignee = Page.Locator("summary[aria-label='Change assignee']");
        await RetryUntil(
            async () =>
            {
                await assignee.ClickAsync();
                await Page.Locator("details[open]").GetByRole(AriaRole.Button, new() { Name = devName }).ClickAsync();
            },
            assignee.Filter(new() { HasTextString = devName }));

        // The developer's bell: one notification for it, opening the story.
        await using var devContext = await Browser.NewContextAsync();
        var devPage = await SignInAsync(devContext, devEmail);
        await devPage.Locator("summary[aria-label='Notifications']").ClickAsync();
        var notification = devPage.GetByText($"assigned you ST-1 {storyTitle}");
        await Expect(notification).ToHaveCountAsync(1);
        await notification.ClickAsync();
        await Expect(devPage).ToHaveURLAsync(storyUrl);
        await Expect(devPage.GetByRole(AriaRole.Heading, new() { Name = storyTitle })).ToBeVisibleAsync();

        await DeleteUserAsync(devEmail);
    }

    private ILocator TemplateOption(string template) =>
        Page.Locator("label.segmented-option", new() { Has = Page.Locator($"input[data-template-option='{template}']") });

    [Test]
    public async Task A_bug_takes_the_projects_defect_template_and_can_override_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateBoardProjectAsync($"E2E board bug template {suffix}");

        // This project's defects start Advanced, so its bugs do too.
        await Page.GotoAsync($"{dashboardUrl}/edit");
        await TemplateOption("Advanced").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}$"));

        await Page.GotoAsync($"{dashboardUrl}/board");
        await Page.Locator("[data-new-item]").ClickAsync();
        var bugFields = Page.Locator("[data-bug-only]");
        var expected = Page.GetByLabel("Expected results");
        await Expect(bugFields).ToBeHiddenAsync(); // a story: one Description box
        await Expect(expected).ToBeHiddenAsync();

        await PickFormSelectAsync("Type", "Bug");
        await Expect(bugFields).ToBeVisibleAsync();
        await Expect(Page.Locator("input[name='Model.Template'][data-template-option='Advanced']")).ToBeCheckedAsync();
        await Expect(expected).ToBeVisibleAsync();
        await Expect(Page.Locator("label[for='body'] span:visible")).ToHaveTextAsync("Repro steps");

        // Override to Simple: back to one Description box; and back to Advanced to fill it in.
        await TemplateOption("Simple").ClickAsync();
        await Expect(expected).ToBeHiddenAsync();
        await Expect(Page.Locator("label[for='body'] span:visible")).ToHaveTextAsync("Description");
        await TemplateOption("Advanced").ClickAsync();

        var title = $"Promo total ignores the discount {suffix}";
        await Page.GetByLabel("Title").FillAsync(title);
        await Page.Locator("#body").FillAsync("1. Apply WELCOME10\n2. Check the total");
        await expected.FillAsync("10% off");
        await Page.GetByLabel("Actual results").FillAsync("Full price");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create item" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-board-section]")).ToHaveCountAsync(3);
        await Expect(Page.Locator("[data-board-section='Expected results']")).ToContainTextAsync("10% off");
        await Expect(Page.Locator("[data-board-section='Actual results']")).ToContainTextAsync("Full price");

        // Edit keeps the bug's own template, and switching it to Simple shows a single Description.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync();
        await Expect(Page.Locator("input[name='Model.Template'][data-template-option='Advanced']")).ToBeCheckedAsync();
        await TemplateOption("Simple").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page.Locator("[data-board-section='Description']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Bugs_come_from_defects_and_links_show_on_both_sides()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateBoardProjectAsync($"E2E board links {suffix}");
        var scenario = $"Pays with a saved card {suffix}";
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Checkout {suffix}", scenario))[0];
        var summary = $"Saved card is declined {suffix}";
        var defectUrl = await RaiseDefectAsync(dashboardUrl, summary);

        // "Create bug from this defect": prefilled, and linked back.
        await Page.Locator("[data-create-bug]").ClickAsync();
        await Expect(Page.Locator("[data-from-defect]")).ToContainTextAsync("D-1");
        await Expect(Page.GetByLabel("Title")).ToHaveValueAsync(summary);
        await Expect(Page.Locator("summary[aria-label='Type']")).ToContainTextAsync("Bug");
        await Expect(Page.Locator("[data-bug-only]")).ToBeVisibleAsync(); // with the defect's own template
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create item" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = summary })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-board-type]")).ToHaveTextAsync("Bug");
        await Expect(Page.Locator("[data-board-link='D-1']")).ToBeVisibleAsync();

        // A story linked to the test case, from the "Link items" modal.
        var storyUrl = await CreateItemAsync(dashboardUrl, "Story", $"Saved cards {suffix}");
        await Page.Locator("[data-dialog-open='link-items-modal']").ClickAsync();
        var modal = Page.Locator("#link-items-modal");
        // The only scope opens on its own.
        await Expect(modal.Locator($"details[data-link-scope='Checkout {suffix}']")).ToHaveAttributeAsync("open", "");
        await modal.Locator("input[data-link-option='T-1']").CheckAsync();
        await modal.Locator("[data-link-selected]").ClickAsync();
        await Expect(Page.Locator("[data-board-link='T-1']")).ToBeVisibleAsync();

        // Each side shows the other.
        await Page.GotoAsync(defectUrl);
        await Expect(Page.Locator("[data-linked-board-item='BG-1']")).ToBeVisibleAsync();
        await Page.GotoAsync(caseUrl);
        await Expect(Page.Locator("[data-linked-board-item='ST-1']")).ToBeVisibleAsync();

        // Unlinking takes it off again.
        await Page.GotoAsync(storyUrl);
        await Page.Locator("[data-board-link='T-1'] [data-unlink]").ClickAsync();
        await Expect(Page.Locator("[data-board-link='T-1']")).ToHaveCountAsync(0);
    }

    private async Task CheckFileAsync(string csv, ILocator rendered)
    {
        await SubmitUntil(
            async () =>
            {
                await Page.SetInputFilesAsync("input[type=file]", new FilePayload
                {
                    Name = "board.csv",
                    MimeType = "text/csv",
                    Buffer = System.Text.Encoding.UTF8.GetBytes(csv),
                });
                await Page.GetByRole(AriaRole.Button, new() { Name = "Validate" }).ClickAsync();
            },
            rendered);
    }

    [Test]
    public async Task Stories_and_bugs_import_from_csv_with_their_tasks()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateBoardProjectAsync($"E2E board import {suffix}");
        var me = await CurrentUserNameAsync();
        await AddSelfToTeamAsync(dashboardUrl);

        await Page.GotoAsync($"{dashboardUrl}/board");
        // "Import CSV" split button: the import page, with the template in its menu.
        await Page.Locator("[data-split-button] summary").ClickAsync();
        await Expect(Page.Locator("[data-split-button] a", new() { HasTextString = "Download template" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Import CSV" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Import stories and bugs" })).ToBeVisibleAsync();

        // The template downloads as a CSV with every column.
        var href = await Page.Locator("[data-board-template]").GetAttributeAsync("href");
        var template = await Page.APIRequest.GetAsync($"{BaseUrl}/{href}"); // links are relative to <base href="/">
        Assert.That(await template.TextAsync(), Does.StartWith("Type,Title,Description,Assigned to,Status,Template"));

        // A file with a mistake: nothing can be imported until it's fixed.
        await CheckFileAsync("Type,Title\nTask,Orphan\n", Page.Locator("[data-import-errors]"));
        await Expect(Page.Locator("[data-import-error]")).ToContainTextAsync("needs a Story or Bug row above it");
        await Expect(Page.Locator("[data-import-confirm]")).ToHaveCountAsync(0);

        var csv = "Type,Title,Description,Assigned to,Status,Template,Expected results,Actual results\n" +
                  $"Story,Imported wallets {suffix},Apple Pay and Google Pay,{me},In progress,,,\n" +
                  "Task,Register the merchant ID,,,Complete,,,\n" +
                  "Task,Google Pay button,,,,,,\n" +
                  $"Bug,Imported total bug {suffix},1. Apply a code,,,Advanced,Discounted,Full price\n";
        await CheckFileAsync(csv, Page.Locator("[data-import-confirm]"));
        await Expect(Page.Locator("[data-import-status]")).ToHaveTextAsync("Ready to import");
        await Expect(Page.Locator("[data-import-count='New tasks']")).ToHaveTextAsync("2");
        await Expect(Page.Locator("[data-import-confirm]")).ToHaveTextAsync("Import 1 story · 1 bug · 2 tasks");
        await Expect(Page.Locator("[data-import-task]")).ToHaveCountAsync(2);

        await Page.Locator("[data-import-confirm]").ClickAsync();
        await Expect(Page.Locator("[data-board-imported]")).ToHaveTextAsync("Imported 1 story, 1 bug and 2 tasks.");
        var story = Column("InProgress").Locator("[data-board-card='ST-1']");
        await Expect(story).ToContainTextAsync($"Imported wallets {suffix}");
        await Expect(story.Locator("[data-board-tasks]")).ToHaveAttributeAsync("data-board-tasks", "1/2");
        await Expect(Column("New").Locator("[data-board-card='BG-1']")).ToBeVisibleAsync();

        // The bug is Advanced with its results; the story's history says it was imported.
        await Column("New").Locator("[data-board-card='BG-1'] [data-board-title]").ClickAsync();
        await Expect(Page.Locator("[data-board-section='Actual results']")).ToContainTextAsync("Full price");
        await Page.GotoAsync($"{dashboardUrl}/board");
        await story.Locator("[data-board-title]").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#history-modal [data-history-kind='Created']")).ToContainTextAsync("imported this item from a CSV file");
    }

    [Test]
    public async Task The_dashboard_actions_and_projects_list_show_the_board()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"E2E board summary {suffix}";
        var dashboardUrl = await CreateBoardProjectAsync(name);
        var me = await CurrentUserNameAsync();
        await AddSelfToTeamAsync(dashboardUrl);

        // One story assigned to me (in progress), one unassigned bug left in New.
        var storyUrl = await CreateItemAsync(dashboardUrl, "Story", $"Board summary story {suffix}");
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change assignee']").ClickAsync();
                await Page.Locator("details[open]").GetByRole(AriaRole.Button, new() { Name = me }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change assignee']").Filter(new() { HasTextString = me }));
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Move to column']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "In progress", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Move to column']").Filter(new() { HasTextString = "In progress" }));
        await CreateItemAsync(dashboardUrl, "Bug", $"Board summary bug {suffix}");

        // Dashboard: the Board card, and the story under Actions needed.
        await Page.GotoAsync(dashboardUrl);
        var card = Page.Locator("[data-board-summary]");
        await Expect(card).ToContainTextAsync("2");
        await Expect(card.Locator("[data-board-count='New']")).ToHaveTextAsync("1 new");
        await Expect(card.Locator("[data-board-count='InProgress']")).ToHaveTextAsync("1 in progress");
        var mine = Page.Locator("[data-actions-board] [data-action-board-item]");
        await Expect(mine).ToHaveCountAsync(1);
        await Expect(mine.First).ToHaveAttributeAsync("data-action-board-item", "ST-1");
        await mine.First.GetByRole(AriaRole.Link).First.ClickAsync();
        Assert.That(Page.Url, Is.EqualTo(storyUrl));

        // All projects (an active project's row): the board's own line of labels, and the action counted.
        await Page.GotoAsync(dashboardUrl);
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change project status']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Active", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change project status']").Filter(new() { HasTextString = "Active" }));
        await Page.GotoAsync($"{BaseUrl}/projects?q={Uri.EscapeDataString(name)}");
        var row = Page.Locator("li").Filter(new() { HasTextString = name });
        await Expect(row.Locator("[data-board-tags] > span")).ToHaveTextAsync(["1 new item", "1 in progress"]);
        await Expect(row).ToContainTextAsync("1 action needed");
    }

    private async Task MoveToColumnAsync(string column)
    {
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Move to column']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = column, Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Move to column']").Filter(new() { HasTextString = column }));
    }

    [Test]
    public async Task Moving_a_story_hands_it_to_the_developer_then_qa_then_closes_it_as_tested()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateBoardProjectAsync($"E2E board workflow {suffix}");
        var me = await CurrentUserNameAsync();
        await AddSelfToTeamAsync(dashboardUrl); // a QA: the only one on this team
        await CreateItemAsync(dashboardUrl, "Story", $"Workflow story {suffix}");
        var assignee = Page.Locator("summary[aria-label='Change assignee']");
        await Expect(assignee).ToContainTextAsync("Unassigned");

        // In progress: it's mine, and I'm its developer.
        await MoveToColumnAsync("In progress");
        await Expect(assignee).ToContainTextAsync(me);
        await Expect(Page.Locator("[data-developed-by]")).ToContainTextAsync(me);

        // Testing: to the team's QA (me again here).
        await MoveToColumnAsync("Testing");
        await Expect(assignee).ToContainTextAsync(me);

        // Closed: unassigned, tested by the QA who had it.
        await MoveToColumnAsync("Closed");
        await Expect(assignee).ToContainTextAsync("Unassigned");
        await Expect(Page.Locator("[data-tested-by]")).ToContainTextAsync(me);

        await Page.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#history-modal [data-history-kind='AssigneeChanged']")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task Comments_link_board_items_with_hash_ids()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateBoardProjectAsync($"E2E board refs {suffix}");
        var storyTitle = $"Wallet support {suffix}";
        var storyUrl = await CreateItemAsync(dashboardUrl, "Story", storyTitle);
        await RaiseDefectAsync(dashboardUrl, $"Wallet button missing {suffix}");

        var box = Page.Locator("#new-comment");
        await box.ClickAsync();
        await box.PressSequentiallyAsync("Fixed under #st");
        await Expect(Page.Locator("[data-reference-option]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-reference-option]").First).ToHaveAttributeAsync("data-reference-option", "ST-1");
        await box.PressAsync("Enter");
        await Expect(box).ToHaveValueAsync("Fixed under #ST-1 ");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();

        var link = Page.Locator("#comments li a[data-comment-ref='ST-1']");
        await Expect(link).ToBeVisibleAsync();
        await link.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = storyTitle })).ToBeVisibleAsync();
        Assert.That(Page.Url, Is.EqualTo(storyUrl));
    }
}
