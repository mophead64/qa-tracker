using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web;
using QaTracker.Web.Attachments;
using QaTracker.Web.Board;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;

namespace QaTracker.UnitTests.Board;

/// <summary>
/// Board items plugged into the rest of the app: files and their history, reactions on board
/// comments, notification choices and links, and the live stamps of the defect and dashboard
/// pages that show board items.
/// </summary>
public sealed class BoardAcrossTheAppTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-19T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly FakeFileStorage storage = new();
    private readonly AttachmentService attachments;
    private readonly NotificationService notifications;
    private readonly ProjectService projects;
    private readonly DefectService defects;
    private readonly BoardService board;
    private readonly Guid projectId;

    public BoardAcrossTheAppTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        factory = new Factory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "qa", UserName = "qa@test.local", FullName = "QA One" });
            db.Users.Add(new ApplicationUser { Id = "dev", UserName = "dev@test.local", FullName = "Dev One" });
            db.Users.Add(new ApplicationUser { Id = "outsider", UserName = "out@test.local", FullName = "Out Sider" });
            db.SaveChanges();
        }

        attachments = new AttachmentService(factory, storage, time, NullLogger<AttachmentService>.Instance);
        notifications = new NotificationService(factory, time);
        projects = new ProjectService(factory, time, attachments, notifications);
        defects = new DefectService(factory, time, projects, attachments, notifications);
        board = new BoardService(factory, new BoardProviders(new LocalBoardProvider(factory, time)), attachments, notifications, time);
        projectId = projects.CreateAsync("Proj", null, null, [], "qa").GetAwaiter().GetResult().Id;
        projects.AddMembersAsync(projectId, ["qa", "dev"]).GetAwaiter().GetResult();
        board.SetModeAsync(projectId, BoardMode.Local).GetAwaiter().GetResult();

        // Start each test with no notifications (joining the team above sends some).
        using var clean = factory.CreateDbContext();
        clean.Notifications.ExecuteDelete();
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    private Task<BoardItem> StoryAsync(string title, string? assignee = null) =>
        board.CreateAsync(projectId, BoardItemType.Story, new BoardItemInput(title, null, assignee), "qa");

    private Task<AttachmentView> UploadAsync(AttachmentOwner owner, Guid ownerId, string fileName, string by = "dev")
    {
        var bytes = Encoding.UTF8.GetBytes("file " + fileName);
        return attachments.UploadAsync(owner, ownerId, new MemoryStream(bytes), fileName, "text/plain", bytes.Length, null, by);
    }

    [Fact]
    public async Task Files_on_an_item_and_its_comments_are_listed_together_and_recorded_in_its_history()
    {
        var story = await StoryAsync("Saved cards");
        var commentId = await board.AddCommentAsync(projectId, story.Id, "qa", "Log attached");
        var spec = await UploadAsync(AttachmentOwner.BoardItem, story.Id, "spec.txt");
        await UploadAsync(AttachmentOwner.BoardItemComment, commentId, "server.log", "qa");

        var files = await attachments.ListForBoardItemAsync(story.Id);
        Assert.Equal(["spec.txt", "server.log"], files.Select(f => f.FileName));
        Assert.Null(files[0].Comment);
        Assert.Equal(commentId, files[1].Comment?.Id);

        await attachments.DeleteAsync(spec.Id, "qa");

        // Files on the item itself are history; files on its comments ride with the comment.
        var history = await board.ListHistoryAsync(story.Id);
        Assert.Contains(history, e => e.Kind == BoardEventKind.FileAdded && e.ToValue == "spec.txt");
        Assert.Contains(history, e => e.Kind == BoardEventKind.FileRemoved && e.FromValue == "spec.txt");
        Assert.DoesNotContain(history, e => e.ToValue == "server.log");
    }

    [Fact]
    public async Task Deleting_a_story_removes_the_files_on_it_its_comments_and_its_tasks()
    {
        var story = await StoryAsync("Saved cards");
        var task = await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("Schema", null, null), "dev", story.Id);
        var taskComment = await board.AddCommentAsync(projectId, task.Id, "dev", "Migration attached");
        await UploadAsync(AttachmentOwner.BoardItem, story.Id, "spec.txt");
        await UploadAsync(AttachmentOwner.BoardItem, task.Id, "schema.sql");
        await UploadAsync(AttachmentOwner.BoardItemComment, taskComment, "migration.sql");
        var other = await StoryAsync("Wallets");
        await UploadAsync(AttachmentOwner.BoardItem, other.Id, "wallets.txt");
        Assert.Equal(4, storage.Count);

        await board.DeleteAsync(projectId, story.Id);

        Assert.Equal(1, storage.Count);
        await using var db = factory.CreateDbContext();
        Assert.Equal(["wallets.txt"], await db.Attachments.Select(a => a.FileName).ToListAsync());
    }

    [Fact]
    public async Task Deleting_the_project_removes_every_board_file()
    {
        var story = await StoryAsync("Saved cards");
        var commentId = await board.AddCommentAsync(projectId, story.Id, "qa", "See log");
        await UploadAsync(AttachmentOwner.BoardItem, story.Id, "spec.txt");
        await UploadAsync(AttachmentOwner.BoardItemComment, commentId, "server.log");

        await attachments.PurgeForProjectAsync(projectId);

        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public async Task Deleting_a_board_comment_takes_its_files_with_it()
    {
        var story = await StoryAsync("Saved cards");
        var commentId = await board.AddCommentAsync(projectId, story.Id, "qa", "See log");
        await UploadAsync(AttachmentOwner.BoardItemComment, commentId, "server.log");

        await board.DeleteCommentAsync(commentId);

        Assert.Empty(await board.ListCommentsAsync(story.Id));
        Assert.Empty(await attachments.ListForBoardItemAsync(story.Id));
        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public async Task Reacting_to_a_board_comment_notifies_its_author_with_a_link_to_the_item()
    {
        var reactions = new CommentReactionService(factory, time, notifications);
        var story = await StoryAsync("Saved cards");
        var commentId = await board.AddCommentAsync(projectId, story.Id, "dev", "Ready for testing");

        Assert.True(await reactions.ReactAsync(CommentKind.BoardItem, commentId, "qa", Reaction.Up));
        Assert.True(await reactions.ReactAsync(CommentKind.BoardItem, commentId, "dev", Reaction.Up)); // own comment: no notification

        var view = Assert.Single(await notifications.ListAsync("dev"));
        Assert.Equal("QA One gave your comment a thumbs up on ST-1 Saved cards", view.Message);
        Assert.Equal(AppUrls.BoardItem(projectId, story.Id), view.Path);
        Assert.Equal("Proj", view.ProjectName);

        // The same reaction again takes it back; the reactions show on the comment.
        Assert.True(await reactions.ReactAsync(CommentKind.BoardItem, commentId, "qa", Reaction.Up));
        var comment = Assert.Single(await board.ListCommentsAsync(story.Id));
        Assert.Equal(["dev"], comment.Reactions.Select(r => r.UserId));

        Assert.False(await reactions.ReactAsync(CommentKind.BoardItem, Guid.NewGuid(), "qa", Reaction.Up));
    }

    [Fact]
    public async Task Board_assignments_follow_the_persons_notification_choices()
    {
        var story = await StoryAsync("Saved cards");
        await using (var db = factory.CreateDbContext())
        {
            var dev = await db.Users.SingleAsync(u => u.Id == "dev");
            dev.NotificationsOff = NotificationTriggers.Bit(NotificationTrigger.BoardItemAssignedToMe);
            await db.SaveChangesAsync();
        }

        await board.SetAssigneeAsync(projectId, story.Id, "dev", "qa");
        Assert.Empty(await notifications.ListAsync("dev"));

        // Assigning yourself never notifies you either.
        await board.SetAssigneeAsync(projectId, story.Id, "qa", "qa");
        Assert.Empty(await notifications.ListAsync("qa"));
    }

    [Fact]
    public async Task Mentions_on_the_board_reach_only_the_team_and_only_while_the_name_is_still_in_the_text()
    {
        var story = await StoryAsync("Saved cards");

        await board.AddCommentAsync(projectId, story.Id, "qa", "@Out Sider have a look", ["outsider"]); // not on the team
        await board.AddCommentAsync(projectId, story.Id, "qa", "never mind", ["dev"]);                   // picked, then deleted
        await board.AddCommentAsync(projectId, story.Id, "qa", "@QA One note to self", ["qa"]);          // yourself

        await using var db = factory.CreateDbContext();
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task The_defect_page_refreshes_when_a_linked_board_item_moves()
    {
        var defect = await defects.CreateAsync(projectId, new DefectInput("Checkout 500s", null, null, null, null), "qa");
        var before = await defects.GetLiveVersionAsync(projectId, defect.Id);

        var bug = await board.CreateAsync(projectId, BoardItemType.Bug, new BoardItemInput("Fix checkout 500", null, null), "qa",
            fromDefectId: defect.Id);
        var linked = await defects.GetLiveVersionAsync(projectId, defect.Id);
        Assert.NotEqual(before, linked);

        await board.MoveAsync(projectId, bug.Id, BoardColumn.InProgress, 0, "dev");
        Assert.NotEqual(linked, await defects.GetLiveVersionAsync(projectId, defect.Id));
    }

    [Fact]
    public async Task The_dashboard_refreshes_when_the_board_changes()
    {
        var start = await projects.GetDashboardLiveVersionAsync(projectId);

        var story = await StoryAsync("Saved cards");
        var added = await projects.GetDashboardLiveVersionAsync(projectId);
        Assert.NotEqual(start, added);

        await board.SetAssigneeAsync(projectId, story.Id, "dev", "qa");
        var assigned = await projects.GetDashboardLiveVersionAsync(projectId);
        Assert.NotEqual(added, assigned);

        await board.SetModeAsync(projectId, BoardMode.Off, "qa");
        Assert.NotEqual(assigned, await projects.GetDashboardLiveVersionAsync(projectId));
    }

    [Fact]
    public async Task Switching_the_board_off_keeps_its_items_for_when_it_comes_back()
    {
        await StoryAsync("Saved cards");
        await board.SetModeAsync(projectId, BoardMode.Off, "qa");

        Assert.False(await board.IsEnabledAsync(projectId));
        Assert.Empty(await board.CountByProjectAsync([projectId]));

        await board.SetModeAsync(projectId, BoardMode.Local, "qa");
        Assert.Equal(["Saved cards"], (await board.ListAsync(projectId)).Select(i => i.Title));
    }

    [Theory]
    [InlineData("ST", BoardItemType.Story)]
    [InlineData("bg", BoardItemType.Bug)]
    [InlineData("Tk", BoardItemType.Task)]
    [InlineData("D", null)]
    [InlineData("", null)]
    public void Reference_prefixes_map_to_item_types_in_any_case(string prefix, BoardItemType? expected)
    {
        Assert.Equal(expected, BoardDisplay.TypeFromPrefix(prefix));
        if (expected is { } type)
        {
            Assert.Equal(prefix.ToUpperInvariant(), BoardDisplay.Prefix(type));
        }
    }

    [Fact]
    public void Names_on_cards_fall_back_sensibly()
    {
        Assert.Equal("Unassigned", BoardDisplay.NameOf(null));
        Assert.Equal("Nobody", BoardDisplay.NameOf(null, "Nobody"));
        Assert.Equal("Dev One", BoardDisplay.NameOf(new ApplicationUser { FullName = "Dev One", UserName = "dev@test.local" }));
        Assert.Equal("dev@test.local", BoardDisplay.NameOf(new ApplicationUser { FullName = " ", UserName = "dev@test.local" }));
        Assert.Equal(ApplicationUser.DeletedName,
            BoardDisplay.NameOf(new ApplicationUser { FullName = "Gone", DeletedUtc = DateTimeOffset.UnixEpoch }));
        Assert.Equal(["New", "In progress", "Testing", "Closed"], BoardDisplay.Columns.Select(BoardDisplay.ColumnLabel));
        Assert.Equal(("Active", "Complete"), (BoardDisplay.TaskState(false), BoardDisplay.TaskState(true)));
    }
}
