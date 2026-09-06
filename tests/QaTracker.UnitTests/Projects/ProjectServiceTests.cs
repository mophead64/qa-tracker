using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;

namespace QaTracker.UnitTests.Projects;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-03T10:00:00Z", CultureInfo.InvariantCulture));
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

        attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private ProjectService CreateSut() => new(factory, time, attachments);

    [Fact]
    public async Task CreateAsync_sets_defaults()
    {
        var sut = CreateSut();

        var project = await sut.CreateAsync("  Acme portal  ", "  some notes  ", [], "user-1");

        Assert.Equal("Acme portal", project.Name);
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

        var project = await sut.CreateAsync("P", "   ", [], "user-1");

        Assert.Null(project.Notes);
    }

    [Fact]
    public async Task CreateAsync_keeps_links_in_order_and_drops_empty_rows()
    {
        var sut = CreateSut();

        var created = await sut.CreateAsync("P", null,
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
        var created = await sut.CreateAsync("P", null, [new("Old", "https://old.test")], "user-1");

        time.Advance(TimeSpan.FromHours(2));
        await sut.UpdateAsync(created.Id, "Renamed", "notes", ProjectStatus.Complete,
            [new("New", "https://new.test")]);

        var project = await sut.GetAsync(created.Id);

        Assert.NotNull(project);
        Assert.Equal("Renamed", project!.Name);
        Assert.Equal(ProjectStatus.Complete, project.Status);
        Assert.Equal(time.GetUtcNow(), project.UpdatedUtc);
        Assert.Equal("New", Assert.Single(project.Links).Label);
    }

    [Fact]
    public async Task ListActiveAsync_excludes_completed_projects()
    {
        var sut = CreateSut();
        var active = await sut.CreateAsync("Active", null, [], "user-1");
        var notStarted = await sut.CreateAsync("Fresh", null, [], "user-1");
        var done = await sut.CreateAsync("Done", null, [], "user-1");
        await sut.MarkInFlightAsync(active.Id);
        await sut.UpdateAsync(done.Id, "Done", null, ProjectStatus.Complete, []);

        var listed = await sut.ListActiveAsync();

        Assert.Equal([active.Id, notStarted.Id], listed.Select(p => p.Id)); // ordered by name: "Active", "Fresh"
        Assert.DoesNotContain(done.Id, listed.Select(p => p.Id));
    }

    [Fact]
    public async Task MarkInFlightAsync_only_promotes_from_not_started()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, [], "user-1");

        await sut.MarkInFlightAsync(created.Id);
        Assert.Equal(ProjectStatus.InFlight, (await sut.GetAsync(created.Id))!.Status);

        await sut.UpdateAsync(created.Id, "P", null, ProjectStatus.Complete, []);
        await sut.MarkInFlightAsync(created.Id);
        Assert.Equal(ProjectStatus.Complete, (await sut.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task DeleteAsync_removes_project_and_links()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, [new("Repo", "https://example.test")], "user-1");

        await sut.DeleteAsync(created.Id);

        Assert.Null(await sut.GetAsync(created.Id));
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.ProjectLinks);
    }

    [Fact]
    public async Task CreateAsync_assigns_team_members()
    {
        var sut = CreateSut();

        var created = await sut.CreateAsync("P", null, [], "user-1", ["user-1", "user-2"]);

        var project = await sut.GetAsync(created.Id);
        Assert.Equal(
            new[] { "user-1", "user-2" }.ToHashSet(),
            project!.Members.Select(m => m.Id).ToHashSet());
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_member_set()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, [], "user-1", ["user-1", "user-2"]);

        await sut.UpdateAsync(created.Id, "P", null, ProjectStatus.NotStarted, [], ["user-2"]);

        var project = await sut.GetAsync(created.Id);
        Assert.Equal("user-2", Assert.Single(project!.Members).Id);
    }

    [Fact]
    public async Task UpdateAsync_with_no_members_clears_the_team()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, [], "user-1", ["user-1"]);

        await sut.UpdateAsync(created.Id, "P", null, ProjectStatus.NotStarted, []);

        var project = await sut.GetAsync(created.Id);
        Assert.Empty(project!.Members);
    }

    [Fact]
    public async Task ListForUserAsync_returns_only_projects_the_user_is_a_member_of()
    {
        var sut = CreateSut();
        var mine = await sut.CreateAsync("Mine", null, [], "user-1", ["user-1"]);
        await sut.CreateAsync("Not mine", null, [], "user-1", ["user-2"]);

        var result = await sut.ListForUserAsync("user-1");

        Assert.Equal(mine.Id, Assert.Single(result).Id);
    }

    public void Dispose() => connection.Dispose();
}
