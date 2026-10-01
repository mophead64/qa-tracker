using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;

namespace QaTracker.UnitTests.Projects;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-03T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly FakeFileStorage storage = new();
    private readonly AttachmentService attachments;

    public ProjectServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        factory = new TestDbContextFactory(options);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        db.Users.AddRange(
            new ApplicationUser { Id = "user-1", UserName = "qa", Email = "qa@test.local" },
            new ApplicationUser { Id = "user-2", UserName = "dev", Email = "dev@test.local" });
        db.SaveChanges();

        attachments = new AttachmentService(factory, storage, time, NullLogger<AttachmentService>.Instance);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private NotificationService Notifications() => new(factory, time);

    private ProjectService CreateSut() => new(factory, time, attachments, Notifications());

    [Fact]
    public async Task CreateAsync_sets_defaults()
    {
        var sut = CreateSut();

        var project = await sut.CreateAsync(
            "  Acme portal  ", "  Rapid portal rebuild  ", "  some notes  ", [], "user-1");

        Assert.Equal("Acme portal", project.Name);
        Assert.Equal("Rapid portal rebuild", project.Brief);
        Assert.Equal("some notes", project.Notes);
        Assert.Equal(ProjectStatus.NotStarted, project.Status);
        Assert.Equal("user-1", project.CreatedById);
        Assert.Equal(time.GetUtcNow(), project.CreatedUtc);
        Assert.Equal(time.GetUtcNow(), project.UpdatedUtc);
    }

    [Fact]
    public async Task CreateAsync_blank_notes_stored_as_null()
    {
        var sut = CreateSut();

        var project = await sut.CreateAsync("P", "   ", "   ", [], "user-1");

        Assert.Null(project.Brief);
        Assert.Null(project.Notes);
    }

    [Fact]
    public async Task CreateAsync_collapses_brief_to_one_line()
    {
        var sut = CreateSut();

        var project = await sut.CreateAsync("P", "One line\r\n  with a pasted break", null, [], "user-1");

        Assert.Equal("One line with a pasted break", project.Brief);
    }

    [Fact]
    public async Task CreateAsync_keeps_links_in_order_and_drops_empty_rows()
    {
        var sut = CreateSut();

        var created = await sut.CreateAsync("P", null, null,
            [new("Repo", "https://example.test/repo"), new("", ""), new("Staging", "https://staging.test")],
            "user-1");

        var project = await sut.GetAsync(created.Id);

        Assert.NotNull(project);
        Assert.Collection(project!.Links,
            l => Assert.Equal(("Repo", 0), (l.Label, l.SortOrder)),
            l => Assert.Equal(("Staging", 1), (l.Label, l.SortOrder)));
    }

    [Fact]
    public async Task UpdateAsync_replaces_links_and_touches_timestamp()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [new("Old", "https://old.test")], "user-1");

        time.Advance(TimeSpan.FromHours(2));
        await sut.UpdateAsync(created.Id, "Renamed", null, "notes",
            [new("New", "https://new.test")]);

        var project = await sut.GetAsync(created.Id);

        Assert.NotNull(project);
        Assert.Equal("Renamed", project!.Name);
        Assert.Equal(time.GetUtcNow(), project.UpdatedUtc);
        Assert.Equal("New", Assert.Single(project.Links).Label);
    }

    [Fact]
    public async Task ListActiveAsync_excludes_completed_projects()
    {
        var sut = CreateSut();
        var active = await sut.CreateAsync("Active", null, null, [], "user-1");
        var notStarted = await sut.CreateAsync("Fresh", null, null, [], "user-1");
        var done = await sut.CreateAsync("Done", null, null, [], "user-1");
        await sut.MarkInFlightAsync(active.Id);
        await sut.SetStatusAsync(done.Id, ProjectStatus.Complete);

        var listed = await sut.ListActiveAsync();

        Assert.Equal([active.Id, notStarted.Id], listed.Select(p => p.Id)); // ordered by name: "Active", "Fresh"
        Assert.DoesNotContain(done.Id, listed.Select(p => p.Id));
    }

    [Fact]
    public async Task MarkInFlightAsync_only_promotes_from_not_started()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        await sut.MarkInFlightAsync(created.Id);
        Assert.Equal(ProjectStatus.InFlight, (await sut.GetAsync(created.Id))!.Status);

        await sut.SetStatusAsync(created.Id, ProjectStatus.Complete);
        await sut.MarkInFlightAsync(created.Id);
        Assert.Equal(ProjectStatus.Complete, (await sut.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task SetStatusAsync_sets_any_status_and_touches_timestamp()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        time.Advance(TimeSpan.FromHours(1));
        await sut.SetStatusAsync(created.Id, ProjectStatus.Complete);
        var done = await sut.GetAsync(created.Id);
        Assert.Equal(ProjectStatus.Complete, done!.Status);
        Assert.Equal(time.GetUtcNow(), done.UpdatedUtc);

        // Unlike MarkInFlightAsync, this can move a project back out of a terminal state.
        await sut.SetStatusAsync(created.Id, ProjectStatus.InFlight);
        Assert.Equal(ProjectStatus.InFlight, (await sut.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task DeleteAsync_removes_project_and_links()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [new("Repo", "https://example.test")], "user-1");

        await sut.DeleteAsync(created.Id);

        Assert.Null(await sut.GetAsync(created.Id));
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.ProjectLinks);
    }

    [Fact]
    public async Task AddMembersAsync_adds_users_and_is_idempotent()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        await sut.AddMembersAsync(created.Id, ["user-1", "user-2"]);
        await sut.AddMembersAsync(created.Id, ["user-1"]); // already there — no-op

        var project = await sut.GetAsync(created.Id);
        Assert.Equal(
            new[] { "user-1", "user-2" }.ToHashSet(),
            project!.Members.Select(m => m.Id).ToHashSet());
    }

    [Fact]
    public async Task RemoveMemberAsync_removes_one_user_and_leaves_the_rest()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.AddMembersAsync(created.Id, ["user-1", "user-2"]);

        await sut.RemoveMemberAsync(created.Id, "user-1");
        await sut.RemoveMemberAsync(created.Id, "user-1"); // gone already — no-op

        var project = await sut.GetAsync(created.Id);
        Assert.Equal("user-2", Assert.Single(project!.Members).Id);
    }

    [Fact]
    public async Task UpdateAsync_leaves_the_team_untouched()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.AddMembersAsync(created.Id, ["user-1"]);

        await sut.UpdateAsync(created.Id, "Renamed", null, null, []);

        var project = await sut.GetAsync(created.Id);
        Assert.Equal("user-1", Assert.Single(project!.Members).Id);
    }

    [Fact]
    public async Task ListForUserAsync_returns_only_projects_the_user_is_a_member_of()
    {
        var sut = CreateSut();
        var mine = await sut.CreateAsync("Mine", null, null, [], "user-1");
        await sut.AddMembersAsync(mine.Id, ["user-1"]);
        var notMine = await sut.CreateAsync("Not mine", null, null, [], "user-1");
        await sut.AddMembersAsync(notMine.Id, ["user-2"]);

        var result = await sut.ListForUserAsync("user-1");

        Assert.Equal(mine.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task Comments_add_list_in_order_and_delete()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("P", null, null, [], "user-1");

        await sut.AddCommentAsync(project.Id, "user-1", "  first  ");
        time.Advance(TimeSpan.FromMinutes(1));
        var dropId = await sut.AddCommentAsync(project.Id, "user-2", "second");

        var comments = await sut.ListCommentsAsync(project.Id);
        Assert.Equal(["first", "second"], comments.Select(c => c.Body));
        Assert.Equal(["qa", "dev"], comments.Select(c => c.AuthorName));

        await sut.DeleteCommentAsync(dropId);
        Assert.Equal("first", Assert.Single(await sut.ListCommentsAsync(project.Id)).Body);
    }

    [Fact]
    public async Task Comments_belong_to_their_own_project()
    {
        var sut = CreateSut();
        var a = await sut.CreateAsync("A", null, null, [], "user-1");
        var b = await sut.CreateAsync("B", null, null, [], "user-1");

        await sut.AddCommentAsync(a.Id, "user-1", "on A");

        Assert.Single(await sut.ListCommentsAsync(a.Id));
        Assert.Empty(await sut.ListCommentsAsync(b.Id));
    }

    [Fact]
    public async Task Mentioning_a_team_member_notifies_them_with_a_link_to_the_dashboard()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("Acme", null, null, [], "user-1");
        await sut.AddMembersAsync(project.Id, ["user-1", "user-2"]);

        await sut.AddCommentAsync(project.Id, "user-1", "Over to you @dev", ["user-2"]);

        var notification = Assert.Single(await Notifications().ListAsync("user-2"));
        Assert.Equal("qa mentioned you in a comment on the project dashboard", notification.Message);
        Assert.Equal("Acme", notification.ProjectName);
        Assert.Equal($"projects/{project.Id}#comments", notification.Path);
        Assert.Empty(await Notifications().ListAsync("user-1"));
    }

    [Fact]
    public async Task Mentioning_someone_off_the_team_notifies_nobody()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("Acme", null, null, [], "user-1");
        await sut.AddMembersAsync(project.Id, ["user-1"]);

        await sut.AddCommentAsync(project.Id, "user-1", "Over to you @dev", ["user-2"]);

        Assert.Empty(await Notifications().ListAsync("user-2"));
    }

    [Fact]
    public async Task Deleting_a_comment_removes_its_files_from_storage()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("P", null, null, [], "user-1");
        var commentId = await sut.AddCommentAsync(project.Id, "user-1", "see attached");
        await attachments.UploadAsync(AttachmentOwner.ProjectComment, commentId, new MemoryStream([1, 2, 3]),
            "shot.png", "image/png", 3, null, "user-1");

        var comment = Assert.Single(await sut.ListCommentsAsync(project.Id));
        Assert.Equal("shot.png", Assert.Single(comment.Attachments).FileName);
        Assert.Empty(await attachments.ListForProjectAsync(project.Id));

        await sut.DeleteCommentAsync(commentId);

        Assert.Equal(0, storage.Count);
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task Deleting_the_project_removes_its_comments_their_files_and_mention_notifications()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.AddMembersAsync(project.Id, ["user-1", "user-2"]);
        var commentId = await sut.AddCommentAsync(project.Id, "user-1", "hi @dev", ["user-2"]);
        await attachments.UploadAsync(AttachmentOwner.ProjectComment, commentId, new MemoryStream([1]),
            "a.txt", "text/plain", 1, null, "user-1");

        await sut.DeleteAsync(project.Id);

        Assert.Equal(0, storage.Count);
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.ProjectComments);
        Assert.Empty(db.Attachments);
        Assert.Empty(db.Notifications);
    }

    public void Dispose() => connection.Dispose();
}
