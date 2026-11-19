using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Board;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;

namespace QaTracker.UnitTests.Board;

/// <summary>
/// What moving a story or bug between columns does to who it's assigned to: In progress → the mover
/// (or back to its developer), Testing → a QA (the same one as before, else the least busy), Closed →
/// unassigned with tested by / closed by recorded — and the new assignee is told.
/// </summary>
public sealed class BoardWorkflowTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-19T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly BoardService board;
    private readonly Guid projectId;

    public BoardWorkflowTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        factory = new Factory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Roles.Add(new IdentityRole(Roles.QA) { Id = "role-qa", NormalizedName = "QA" });
            db.Roles.Add(new IdentityRole(Roles.Dev) { Id = "role-dev", NormalizedName = "DEV" });
            foreach (var (id, name, role) in new[]
                     {
                         ("ann", "Ann QA", "role-qa"), ("ben", "Ben QA", "role-qa"),
                         ("dev1", "Dev One", "role-dev"), ("dev2", "Dev Two", "role-dev"),
                     })
            {
                db.Users.Add(new ApplicationUser { Id = id, UserName = $"{id}@test.local", FullName = name });
                db.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = role });
            }
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        var projects = new ProjectService(factory, time, attachments, notifications);
        board = new BoardService(factory, new BoardProviders(new LocalBoardProvider(factory, time)), attachments, notifications, time);
        projectId = projects.CreateAsync("Proj", null, null, [], "ann").GetAwaiter().GetResult().Id;
        projects.AddMembersAsync(projectId, ["ann", "ben", "dev1", "dev2"]).GetAwaiter().GetResult();
        board.SetModeAsync(projectId, BoardMode.Local).GetAwaiter().GetResult();
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    private async Task<BoardItem> StoryAsync(string title = "Saved cards", string? assignee = null) =>
        await board.CreateAsync(projectId, BoardItemType.Story, new BoardItemInput(title, null, assignee), "ann");

    private async Task<BoardItem> GetAsync(Guid id) => (await board.GetAsync(projectId, id))!;

    private Task MoveAsync(BoardItem item, BoardColumn column, string who) => board.MoveAsync(projectId, item.Id, column, 0, who);

    private async Task<IReadOnlyList<string>> BoardNotificationsForAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Notifications.Where(n => n.UserId == userId && n.BoardItemId != null).Select(n => n.Message).ToListAsync();
    }

    [Fact]
    public async Task The_full_round_trip_assigns_the_developer_then_a_qa_then_back_and_closes_as_tested()
    {
        var story = await StoryAsync();

        // Picked up: whoever moves it into In progress gets it and becomes its developer.
        await MoveAsync(story, BoardColumn.InProgress, "dev1");
        var item = await GetAsync(story.Id);
        Assert.Equal(("dev1", "dev1"), (item.AssignedToId, item.DevelopedById));
        Assert.Empty(await BoardNotificationsForAsync("dev1")); // you don't hear about your own move

        // Ready for testing: to a QA (Ann, by name, with nobody busier), who's told.
        await MoveAsync(story, BoardColumn.Testing, "dev1");
        item = await GetAsync(story.Id);
        Assert.Equal(("ann", "ann"), (item.AssignedToId, item.QaById));
        Assert.Equal(["Dev One assigned you ST-1 Saved cards"], await BoardNotificationsForAsync("ann"));

        // Sent back: to the developer.
        await MoveAsync(story, BoardColumn.InProgress, "ann");
        Assert.Equal("dev1", (await GetAsync(story.Id)).AssignedToId);
        Assert.Single(await BoardNotificationsForAsync("dev1"));

        // Back to Testing: the same QA, even though Ben now has less on.
        await MoveAsync(story, BoardColumn.Testing, "dev1");
        Assert.Equal("ann", (await GetAsync(story.Id)).AssignedToId);

        // Closed out of Testing: unassigned, tested by the QA who had it.
        await MoveAsync(story, BoardColumn.Closed, "ann");
        item = await GetAsync(story.Id);
        Assert.Null(item.AssignedToId);
        Assert.Equal(("ann", null), (item.TestedById, item.ClosedById));
        Assert.Equal("Ann QA", item.TestedBy?.FullName);

        // Every hand-off is in its history.
        var assignments = (await board.ListHistoryAsync(story.Id)).Count(e => e.Kind == BoardEventKind.AssigneeChanged);
        Assert.Equal(5, assignments);
    }

    [Fact]
    public async Task Testing_goes_to_the_least_busy_qa()
    {
        await StoryAsync("Ann's already", "ann");
        var story = await StoryAsync("Next up");
        await MoveAsync(story, BoardColumn.InProgress, "dev2");

        await MoveAsync(story, BoardColumn.Testing, "dev2");

        Assert.Equal("ben", (await GetAsync(story.Id)).AssignedToId);
    }

    [Fact]
    public async Task Whoever_had_it_in_progress_counts_as_the_developer()
    {
        // Assigned by hand and placed in progress, never picked up through the board.
        var story = await board.CreateAsync(projectId, BoardItemType.Story, new BoardItemInput("Wallets", null, "dev2"), "ann",
            options: new BoardCreateOptions(BoardColumn.InProgress));

        await MoveAsync(story, BoardColumn.Testing, "ann");
        await MoveAsync(story, BoardColumn.InProgress, "ann");

        Assert.Equal("dev2", (await GetAsync(story.Id)).AssignedToId);
    }

    [Fact]
    public async Task Closing_with_nobody_testing_records_who_closed_it_and_reopening_goes_back_to_the_developer()
    {
        var story = await StoryAsync();
        await MoveAsync(story, BoardColumn.InProgress, "dev1");
        await board.SetAssigneeAsync(projectId, story.Id, null, "ann");

        await MoveAsync(story, BoardColumn.Closed, "dev2");
        var item = await GetAsync(story.Id);
        Assert.Equal((null, "dev2"), (item.TestedById, item.ClosedById));

        // Reopened: the developer gets it back, and closed by is cleared.
        await MoveAsync(story, BoardColumn.InProgress, "ann");
        item = await GetAsync(story.Id);
        Assert.Equal(("dev1", null), (item.AssignedToId, item.ClosedById));
    }

    [Fact]
    public async Task Straight_from_new_to_closed_is_closed_by_the_mover_and_new_keeps_the_assignee()
    {
        var story = await StoryAsync(assignee: "dev1");
        await MoveAsync(story, BoardColumn.Closed, "dev2");
        Assert.Equal((null, "dev2", (string?)null), ((await GetAsync(story.Id)).AssignedToId, (await GetAsync(story.Id)).ClosedById,
            (await GetAsync(story.Id)).TestedById));

        var other = await StoryAsync("Parked", "dev1");
        await MoveAsync(other, BoardColumn.InProgress, "dev1");
        await MoveAsync(other, BoardColumn.New, "ann");
        Assert.Equal("dev1", (await GetAsync(other.Id)).AssignedToId);
    }

    [Fact]
    public async Task Reordering_within_a_column_changes_nobody()
    {
        var story = await StoryAsync(assignee: "dev1");
        await StoryAsync("Another");

        await board.MoveAsync(projectId, story.Id, BoardColumn.New, 1, "ann");

        Assert.Equal("dev1", (await GetAsync(story.Id)).AssignedToId);
        Assert.DoesNotContain(await board.ListHistoryAsync(story.Id), e => e.Kind == BoardEventKind.Moved);
    }
}
