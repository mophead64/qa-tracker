using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Comments;

public sealed class CommentReactionServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-10-01T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly DefectService defects;
    private readonly TestCaseService testCases;
    private readonly NotificationService notifications;
    private readonly CommentReactionService sut;
    private readonly Guid projectId;
    private readonly Guid defectId;
    private readonly Guid testCaseId;

    public CommentReactionServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        factory = new TestDbContextFactory(options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.AddRange(
                new ApplicationUser { Id = "user-1", UserName = "qa@test.local", FullName = "QA One" },
                new ApplicationUser { Id = "user-2", UserName = "dev@test.local", FullName = "Dev Two" },
                new ApplicationUser { Id = "user-3", UserName = "dev3@test.local" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        notifications = new NotificationService(factory, time);
        projects = new ProjectService(factory, time, attachments, notifications);
        defects = new DefectService(factory, time, projects, attachments, notifications);
        testCases = new TestCaseService(factory, time, attachments, notifications);
        sut = new CommentReactionService(factory, time, notifications);

        projectId = projects.CreateAsync("Proj", null, null, [], "user-1").GetAwaiter().GetResult().Id;
        defectId = defects.CreateAsync(projectId, new DefectInput("Broken", null, null, null, null), "user-1")
            .GetAwaiter().GetResult().Id;
        var scopeId = new TestScopeService(factory, time, projects, attachments)
            .CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1").GetAwaiter().GetResult().Id;
        testCaseId = testCases.CreateAsync(scopeId, new TestCaseInput("A scenario", null), "user-1")
            .GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private async Task<IReadOnlyList<CommentReactionView>> ProjectReactionsAsync(Guid commentId) =>
        (await projects.ListCommentsAsync(projectId)).Single(c => c.Id == commentId).Reactions;

    [Fact]
    public async Task Reacting_adds_a_reaction_and_the_same_one_again_takes_it_back()
    {
        var commentId = await projects.AddCommentAsync(projectId, "user-1", "hello");

        Assert.True(await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Up));
        var reaction = Assert.Single(await ProjectReactionsAsync(commentId));
        Assert.Equal(new CommentReactionView("user-2", "Dev Two", Reaction.Up), reaction);

        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Up);
        Assert.Empty(await ProjectReactionsAsync(commentId));
    }

    [Fact]
    public async Task Reacting_the_other_way_switches_rather_than_adding_a_second()
    {
        var commentId = await projects.AddCommentAsync(projectId, "user-1", "hello");

        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Up);
        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Down);

        Assert.Equal(Reaction.Down, Assert.Single(await ProjectReactionsAsync(commentId)).Reaction);
    }

    [Fact]
    public async Task Each_user_has_their_own_reaction_listed_in_the_order_they_reacted()
    {
        var commentId = await projects.AddCommentAsync(projectId, "user-1", "hello");

        await sut.ReactAsync(CommentKind.Project, commentId, "user-3", Reaction.Up);
        time.Advance(TimeSpan.FromSeconds(1));
        await sut.ReactAsync(CommentKind.Project, commentId, "user-1", Reaction.Up);
        time.Advance(TimeSpan.FromSeconds(1));
        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Down);

        var reactions = await ProjectReactionsAsync(commentId);
        Assert.Equal(
            [("dev3@test.local", Reaction.Up), ("QA One", Reaction.Up), ("Dev Two", Reaction.Down)],
            reactions.Select(r => (r.UserName, r.Reaction)));
    }

    [Fact]
    public async Task Reactions_on_defect_and_test_case_comments_stay_on_their_own_comment()
    {
        var defectComment = await defects.AddCommentAsync(defectId, "user-1", "on the defect");
        var caseComment = await testCases.AddCommentAsync(testCaseId, "user-1", "on the case");

        await sut.ReactAsync(CommentKind.Defect, defectComment, "user-2", Reaction.Up);
        await sut.ReactAsync(CommentKind.TestCase, caseComment, "user-2", Reaction.Down);

        Assert.Equal(Reaction.Up, Assert.Single(Assert.Single(await defects.ListCommentsAsync(defectId)).Reactions).Reaction);
        Assert.Equal(Reaction.Down, Assert.Single(Assert.Single(await testCases.ListCommentsAsync(testCaseId)).Reactions).Reaction);
    }

    [Fact]
    public async Task Reacting_to_a_missing_comment_or_with_the_wrong_kind_does_nothing()
    {
        var commentId = await projects.AddCommentAsync(projectId, "user-1", "hello");

        Assert.False(await sut.ReactAsync(CommentKind.Project, Guid.NewGuid(), "user-2", Reaction.Up));
        Assert.False(await sut.ReactAsync(CommentKind.Defect, commentId, "user-2", Reaction.Up));

        await using var db = factory.CreateDbContext();
        Assert.Empty(db.CommentReactions);
    }

    [Fact]
    public async Task Deleting_the_comment_or_the_user_removes_their_reactions()
    {
        var keep = await projects.AddCommentAsync(projectId, "user-1", "keep");
        var drop = await projects.AddCommentAsync(projectId, "user-1", "drop");
        await sut.ReactAsync(CommentKind.Project, keep, "user-2", Reaction.Up);
        await sut.ReactAsync(CommentKind.Project, keep, "user-3", Reaction.Up);
        await sut.ReactAsync(CommentKind.Project, drop, "user-2", Reaction.Up);

        await projects.DeleteCommentAsync(drop);
        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(2, await db.CommentReactions.CountAsync());
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == "user-3"));
            await db.SaveChangesAsync();
        }

        Assert.Equal("user-2", Assert.Single(await ProjectReactionsAsync(keep)).UserId);
    }

    [Fact]
    public async Task A_reaction_notifies_the_comment_author_with_a_link_to_the_defect()
    {
        var commentId = await defects.AddCommentAsync(defectId, "user-1", "on the defect");

        await sut.ReactAsync(CommentKind.Defect, commentId, "user-2", Reaction.Up);

        var notification = Assert.Single(await notifications.ListAsync("user-1"));
        Assert.Equal("Dev Two gave your comment a thumbs up on D-1: Broken", notification.Message);
        Assert.Equal($"projects/{projectId}/defects/{defectId}", notification.Path);
        Assert.Empty(await notifications.ListAsync("user-2"));
    }

    [Fact]
    public async Task Test_case_and_project_comment_reactions_link_to_their_own_page()
    {
        var caseComment = await testCases.AddCommentAsync(testCaseId, "user-1", "on the case");
        var projectComment = await projects.AddCommentAsync(projectId, "user-1", "on the project");

        await sut.ReactAsync(CommentKind.TestCase, caseComment, "user-2", Reaction.Down);
        time.Advance(TimeSpan.FromSeconds(1));
        await sut.ReactAsync(CommentKind.Project, projectComment, "user-2", Reaction.Up);

        var list = await notifications.ListAsync("user-1");
        Assert.Equal(
            ["Dev Two gave your comment a thumbs up on the project dashboard",
             "Dev Two gave your comment a thumbs down on a test case: A scenario"],
            list.Select(n => n.Message));
        Assert.Equal($"projects/{projectId}#comments", list[0].Path);
        Assert.EndsWith($"/cases/{testCaseId}", list[1].Path);
    }

    [Fact]
    public async Task Reacting_to_your_own_comment_notifies_nobody()
    {
        var commentId = await projects.AddCommentAsync(projectId, "user-1", "mine");

        await sut.ReactAsync(CommentKind.Project, commentId, "user-1", Reaction.Up);

        Assert.Empty(await notifications.ListAsync("user-1"));
    }

    [Fact]
    public async Task Taking_a_reaction_back_or_toggling_it_again_while_unread_adds_no_more_notifications()
    {
        var commentId = await projects.AddCommentAsync(projectId, "user-1", "hello");

        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Up);
        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Up);
        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Up);
        Assert.Single(await notifications.ListAsync("user-1"));

        // Once read, a fresh reaction is news again; switching is news straight away.
        await notifications.MarkAllReadAsync("user-1");
        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Down);
        await sut.ReactAsync(CommentKind.Project, commentId, "user-2", Reaction.Up);

        Assert.Equal(3, (await notifications.ListAsync("user-1")).Count);
    }

    public void Dispose() => connection.Dispose();
}
