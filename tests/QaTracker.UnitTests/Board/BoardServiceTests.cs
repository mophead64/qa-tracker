using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Board;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Board;

/// <summary>The development board: per-type numbering, columns and ordering, tasks, links, comments and history.</summary>
public sealed class BoardServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-19T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly BoardService board;
    private readonly Guid projectId;

    public BoardServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        factory = new Factory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "qa", UserName = "qa@test.local", FullName = "QA One" });
            db.Users.Add(new ApplicationUser { Id = "dev", UserName = "dev@test.local", FullName = "Dev One" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        projects = new ProjectService(factory, time, attachments, notifications);
        scopes = new TestScopeService(factory, time, projects, attachments);
        cases = new TestCaseService(factory, time, attachments, notifications);
        defects = new DefectService(factory, time, projects, attachments, notifications);
        board = new BoardService(factory, new BoardProviders(new LocalBoardProvider(factory, time)), attachments, notifications, time);
        projectId = projects.CreateAsync("Proj", null, null, [], "qa").GetAwaiter().GetResult().Id;
        projects.AddMembersAsync(projectId, ["qa", "dev"]).GetAwaiter().GetResult();
        board.SetModeAsync(projectId, BoardMode.Local).GetAwaiter().GetResult();
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    private Task<BoardItem> StoryAsync(string title, string? assignee = null) =>
        board.CreateAsync(projectId, BoardItemType.Story, new BoardItemInput(title, null, assignee), "qa");

    private async Task<BoardItem> GetAsync(Guid id) => (await board.GetAsync(projectId, id))!;

    [Fact]
    public async Task Switching_the_board_on_and_off_is_its_own_project_history_entry()
    {
        // The constructor switched it on with no actor; switch it off and on again as a person.
        Assert.False(await board.SetModeAsync(projectId, BoardMode.Local, "qa")); // already on: nothing recorded
        Assert.True(await board.SetModeAsync(projectId, BoardMode.Off, "qa"));
        Assert.True(await board.SetModeAsync(projectId, BoardMode.Local, "dev"));

        await using var db = await factory.CreateDbContextAsync();
        var kinds = (await db.ProjectEvents.Where(e => e.ProjectId == projectId).ToListAsync())
            .Where(e => e.Kind is ProjectEventKind.BoardEnabled or ProjectEventKind.BoardDisabled)
            .Select(e => (e.Kind, e.ActorId))
            .ToList();
        Assert.Equal([(ProjectEventKind.BoardEnabled, (string?)null), (ProjectEventKind.BoardDisabled, "qa"), (ProjectEventKind.BoardEnabled, "dev")], kinds);
        Assert.DoesNotContain(await db.ProjectEvents.Where(e => e.ProjectId == projectId).ToListAsync(),
            e => e.Kind == ProjectEventKind.Edited);
    }

    [Fact]
    public async Task Bugs_keep_a_defect_style_template_and_stories_ignore_it()
    {
        var bug = await board.CreateAsync(projectId, BoardItemType.Bug,
            new BoardItemInput("Card declined", "1. Pay\n2. See it fail", null, DefectTemplate.Advanced, "Paid", "Declined"), "qa");
        var plainBug = await board.CreateAsync(projectId, BoardItemType.Bug, new BoardItemInput("Typo", "Basket", null), "qa");
        var story = await board.CreateAsync(projectId, BoardItemType.Story,
            new BoardItemInput("Wallets", "Apple Pay", null, DefectTemplate.Advanced, "x", "y"), "qa");

        bug = await GetAsync(bug.Id);
        Assert.Equal(DefectTemplate.Advanced, bug.Template);
        Assert.Equal(("Paid", "Declined"), (bug.ExpectedResults, bug.ActualResults));
        Assert.Equal(DefectTemplate.Simple, (await GetAsync(plainBug.Id)).Template); // no template given: Simple
        story = await GetAsync(story.Id);
        Assert.Null(story.Template);
        Assert.Null(story.ExpectedResults);

        // Switching to Simple and changing a field is one "edited" entry naming both.
        await board.UpdateAsync(projectId, bug.Id,
            new BoardItemInput("Card declined", "1. Pay\n2. See it fail", null, DefectTemplate.Simple, "Paid at once", "Declined"), "dev");
        Assert.Equal(DefectTemplate.Simple, (await GetAsync(bug.Id)).Template);
        var edit = (await board.ListHistoryAsync(bug.Id)).First(e => e.Kind == BoardEventKind.Edited);
        Assert.Equal("template and expected results", edit.ToValue);
    }

    [Fact]
    public async Task Counts_cover_stories_and_bugs_per_column_and_open_tasks_for_boards_that_are_on()
    {
        var s1 = await StoryAsync("One");
        var s2 = await StoryAsync("Two");
        await board.CreateAsync(projectId, BoardItemType.Bug, new BoardItemInput("Bug", null, null), "qa");
        await board.MoveAsync(projectId, s2.Id, BoardColumn.Testing, 0, "qa");
        var t1 = await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("T1", null, null), "qa", s1.Id);
        await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("T2", null, null), "qa", s1.Id);
        await board.SetDoneAsync(projectId, t1.Id, true, "qa");
        var other = (await projects.CreateAsync("Board off", null, null, [], "qa")).Id;

        var counts = await board.CountByProjectAsync([projectId, other]);

        Assert.Equal(new BoardCounts(2, 0, 1, 0, 1), counts[projectId]); // New: story + bug; Testing: story; 1 task to do
        Assert.Equal(3, counts[projectId].Open);
        Assert.False(counts.ContainsKey(other)); // no board, no counts
    }

    [Fact]
    public async Task Actions_list_your_unfinished_board_items_furthest_along_first()
    {
        // Placed straight into their columns (moving them would run the workflow's reassignments).
        await board.CreateAsync(projectId, BoardItemType.Story, new BoardItemInput("Mine, testing", null, "dev"), "qa",
            options: new BoardCreateOptions(BoardColumn.Testing));
        var closed = await board.CreateAsync(projectId, BoardItemType.Story, new BoardItemInput("Mine, closed", null, "dev"), "qa",
            options: new BoardCreateOptions(BoardColumn.Closed));
        await board.CreateAsync(projectId, BoardItemType.Bug, new BoardItemInput("Mine, new", null, "dev"), "qa");
        await StoryAsync("Someone else's", "qa");
        var done = await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("Done task", null, "dev"), "qa", closed.Id);
        await board.SetDoneAsync(projectId, done.Id, true, "qa");
        await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("Open task", null, "dev"), "qa", closed.Id);

        var actions = await new QaTracker.Web.Dashboard.ProjectActionsService(factory).GetAsync(projectId, "dev");

        // The open task sits with its (closed) story, ahead of the story in Testing and the new bug.
        Assert.Equal(["Open task", "Mine, testing", "Mine, new"], actions.BoardItems.Select(i => i.Title));
        Assert.Equal(3, actions.Count);

        await board.SetModeAsync(projectId, BoardMode.Off, "qa");
        Assert.Empty((await new QaTracker.Web.Dashboard.ProjectActionsService(factory).GetAsync(projectId, "dev")).BoardItems);
    }

    [Fact]
    public async Task Items_are_numbered_per_type()
    {
        var s1 = await StoryAsync("Saved cards");
        var b1 = await board.CreateAsync(projectId, BoardItemType.Bug, new BoardItemInput("Card form crashes", null, null), "qa");
        var s2 = await StoryAsync("Wallets");
        var t1 = await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("Add table", null, null), "dev", s1.Id);

        Assert.Equal(["ST-1", "BG-1", "ST-2", "TK-1"], new[] { s1, b1, s2, t1 }.Select(BoardDisplay.Ref));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("Orphan", null, null), "dev"));
    }

    [Fact]
    public async Task Cards_move_between_columns_and_keep_their_place()
    {
        var a = await StoryAsync("A");
        var b = await StoryAsync("B");
        var c = await StoryAsync("C");

        await board.MoveAsync(projectId, c.Id, BoardColumn.New, 0, "qa");          // reorder within New
        await board.MoveAsync(projectId, a.Id, BoardColumn.InProgress, 0, "dev");  // move across

        var list = await board.ListAsync(projectId);
        Assert.Equal(["C", "B"], list.Where(i => i.Column == BoardColumn.New).Select(i => i.Title));
        Assert.Equal(["A"], list.Where(i => i.Column == BoardColumn.InProgress).Select(i => i.Title));
        var history = await board.ListHistoryAsync(a.Id);
        Assert.Contains(history, e => e.Kind == BoardEventKind.Moved && e.FromValue == "New" && e.ToValue == "In progress");
    }

    [Fact]
    public async Task Tasks_are_ticked_off_reordered_and_recorded_on_their_story()
    {
        var story = await StoryAsync("Saved cards");
        var t1 = await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("Schema", null, null), "dev", story.Id);
        var t2 = await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("UI", null, "dev"), "dev", story.Id);

        await board.SetDoneAsync(projectId, t1.Id, true, "dev");
        await board.MoveTaskAsync(projectId, t2.Id, 0);

        var loaded = await GetAsync(story.Id);
        Assert.Equal(["UI", "Schema"], loaded.Tasks.Select(t => t.Title));
        Assert.True(loaded.Tasks.Single(t => t.Id == t1.Id).Done);
        var history = await board.ListHistoryAsync(story.Id);
        Assert.Contains(history, e => e.Kind == BoardEventKind.TaskAdded && e.ToValue == "TK-2 UI");
        Assert.Contains(history, e => e.Kind == BoardEventKind.TaskCompleted && e.ToValue == "TK-1 Schema");
    }

    [Fact]
    public async Task Links_stay_inside_the_project_and_show_from_the_other_side()
    {
        var story = await StoryAsync("Saved cards");
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Payment", "qa");
        var tc = await cases.CreateAsync(scope.Id, new TestCaseInput("Pay with a saved card", null), "qa");
        var defect = await defects.CreateAsync(projectId, new DefectInput("Saved card declined", null, null, null, null), "qa");
        var otherProject = (await projects.CreateAsync("Other", null, null, [], "qa")).Id;
        var foreign = await defects.CreateAsync(otherProject, new DefectInput("Elsewhere", null, null, null, null), "qa");

        await board.LinkAsync(projectId, story.Id, BoardLinkKind.Scope, scope.Id, "qa");
        await board.LinkAsync(projectId, story.Id, BoardLinkKind.TestCase, tc.Id, "qa");
        await board.LinkAsync(projectId, story.Id, BoardLinkKind.Defect, defect.Id, "qa");
        await board.LinkAsync(projectId, story.Id, BoardLinkKind.Defect, defect.Id, "qa");   // twice: once
        await board.LinkAsync(projectId, story.Id, BoardLinkKind.Defect, foreign.Id, "qa");  // another project: ignored

        var links = await board.ListLinksAsync(story.Id);
        Assert.Equal(["S-1", "T-1", "D-1"], links.Select(l => l.Ref));
        Assert.Equal(["ST-1"], (await board.ListForDefectAsync(defect.Id)).Select(i => i.Ref));
        Assert.Equal(["ST-1"], (await board.ListForTestCaseAsync(tc.Id, scope.Id)).Select(i => i.Ref));

        await board.UnlinkAsync(projectId, story.Id, links.Single(l => l.Kind == BoardLinkKind.Defect).Id, "qa");
        Assert.Empty(await board.ListForDefectAsync(defect.Id));
        Assert.Contains(await board.ListHistoryAsync(story.Id), e => e.Kind == BoardEventKind.Unlinked && e.FromValue == "D-1 Saved card declined");
    }

    [Fact]
    public async Task A_bug_raised_from_a_defect_is_linked_to_it()
    {
        var defect = await defects.CreateAsync(projectId, new DefectInput("Checkout 500s", null, null, null, null), "qa");

        var bug = await board.CreateAsync(projectId, BoardItemType.Bug, new BoardItemInput("Fix checkout 500", null, null), "qa",
            fromDefectId: defect.Id);

        Assert.Equal(["BG-1"], (await board.ListForDefectAsync(defect.Id)).Select(i => i.Ref));
        Assert.Equal(BoardItemType.Bug, bug.Type);
    }

    [Fact]
    public async Task Deleting_a_story_takes_its_tasks_and_comments()
    {
        var story = await StoryAsync("Saved cards");
        var task = await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("Schema", null, null), "dev", story.Id);
        await board.AddCommentAsync(projectId, story.Id, "qa", "Looks good");

        await board.DeleteAsync(projectId, story.Id);

        Assert.Null(await board.GetAsync(projectId, story.Id));
        Assert.Null(await board.GetAsync(projectId, task.Id));
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.BoardItemComments);
    }

    [Fact]
    public async Task Assigning_notifies_the_assignee_and_mentions_notify_from_comments()
    {
        var story = await StoryAsync("Saved cards");
        await board.SetAssigneeAsync(projectId, story.Id, "dev", "qa");
        await board.AddCommentAsync(projectId, story.Id, "dev", "@QA One can you test this?", ["qa"]);

        await using var db = factory.CreateDbContext();
        Assert.Contains(db.Notifications, n => n.UserId == "dev" && n.BoardItemId == story.Id && n.Message == "QA One assigned you ST-1 Saved cards");
        Assert.Contains(db.Notifications, n => n.UserId == "qa" && n.BoardItemId == story.Id && n.Message.Contains("mentioned you"));
    }

    [Fact]
    public async Task Live_stamps_move_with_the_board_and_the_item()
    {
        var story = await StoryAsync("Saved cards");
        var boardBefore = await board.GetBoardLiveVersionAsync(projectId);
        var itemBefore = await board.GetItemLiveVersionAsync(projectId, story.Id);

        await board.MoveAsync(projectId, story.Id, BoardColumn.Testing, 0, "qa");
        Assert.NotEqual(boardBefore, await board.GetBoardLiveVersionAsync(projectId));
        Assert.NotEqual(itemBefore, await board.GetItemLiveVersionAsync(projectId, story.Id));

        var afterMove = await board.GetItemLiveVersionAsync(projectId, story.Id);
        await board.AddCommentAsync(projectId, story.Id, "qa", "Testing now");
        Assert.NotEqual(afterMove, await board.GetItemLiveVersionAsync(projectId, story.Id));
        Assert.Null(await board.GetItemLiveVersionAsync(projectId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_a_user_unassigns_their_board_items()
    {
        var story = await StoryAsync("Saved cards", assignee: "dev");
        await using (var db = factory.CreateDbContext())
        {
            await UserService.ReleaseFromWorkAsync(db, "dev");
        }

        Assert.Null((await GetAsync(story.Id)).AssignedToId);
    }
}
