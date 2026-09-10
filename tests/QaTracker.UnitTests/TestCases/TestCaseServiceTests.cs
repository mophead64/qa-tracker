using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

public sealed class TestCaseServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-03T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly AttachmentService attachments;
    private readonly Guid scopeId;

    public TestCaseServiceTests()
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
            db.Users.Add(new ApplicationUser
            {
                Id = "user-1",
                UserName = "qa@test.local",
                Email = "qa@test.local",
                FullName = "QA One",
            });
            db.SaveChanges();
        }

        attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var projects = new ProjectService(factory, time, attachments);
        var projectId = projects.CreateAsync("Proj", null, null, [], "user-1").GetAwaiter().GetResult().Id;
        var scopes = new TestScopeService(factory, time, projects, attachments);
        scopeId = scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1").GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private TestCaseService CreateSut() => new(factory, time, attachments);

    private static TestCaseInput Input(
        string scenario = "When a user signs in with valid details",
        string? steps = null) => new(scenario, steps);

    [Fact]
    public async Task CreateAsync_sets_defaults_and_trims()
    {
        var sut = CreateSut();

        var tc = await sut.CreateAsync(scopeId, Input("  does thing  ", "  a\nb  "), "user-1");

        Assert.Equal("does thing", tc.Scenario);
        Assert.Equal("a\nb", tc.Steps);
        Assert.Equal(TestResult.NotRun, tc.Result);
        Assert.Equal(scopeId, tc.TestScopeId);
        Assert.Equal("user-1", tc.CreatedById);
        Assert.Equal(time.GetUtcNow(), tc.CreatedUtc);
    }

    [Fact]
    public async Task CreateAsync_blank_steps_stored_as_null()
    {
        var sut = CreateSut();

        var tc = await sut.CreateAsync(scopeId, Input(steps: "   "), "user-1");

        Assert.Null(tc.Steps);
    }

    [Fact]
    public async Task SetResultAsync_updates_result_and_timestamp()
    {
        var sut = CreateSut();
        var tc = await sut.CreateAsync(scopeId, Input(), "user-1");

        time.Advance(TimeSpan.FromMinutes(30));
        await sut.SetResultAsync(tc.Id, TestResult.Failed);

        var reloaded = await sut.GetAsync(tc.Id);
        Assert.Equal(TestResult.Failed, reloaded!.Result);
        Assert.Equal(time.GetUtcNow(), reloaded.UpdatedUtc);
    }

    [Fact]
    public async Task UpdateAsync_changes_editable_fields_but_not_result()
    {
        var sut = CreateSut();
        var tc = await sut.CreateAsync(scopeId, Input(), "user-1");
        await sut.SetResultAsync(tc.Id, TestResult.Passed);

        await sut.UpdateAsync(tc.Id, Input("After too many attempts the account locks", "lock the account"));

        var reloaded = await sut.GetAsync(tc.Id);
        Assert.Equal("After too many attempts the account locks", reloaded!.Scenario);
        Assert.Equal("lock the account", reloaded.Steps);
        Assert.Equal(TestResult.Passed, reloaded.Result);
    }

    [Fact]
    public async Task ListForScopeAsync_orders_by_created()
    {
        var sut = CreateSut();
        var first = await sut.CreateAsync(scopeId, Input("first"), "user-1");
        time.Advance(TimeSpan.FromMinutes(1));
        var second = await sut.CreateAsync(scopeId, Input("second"), "user-1");

        var list = await sut.ListForScopeAsync(scopeId);

        Assert.Equal([first.Id, second.Id], list.Select(x => x.Id));
    }

    [Fact]
    public async Task DeleteAsync_removes_the_test_case()
    {
        var sut = CreateSut();
        var tc = await sut.CreateAsync(scopeId, Input(), "user-1");

        await sut.DeleteAsync(tc.Id);

        Assert.Null(await sut.GetAsync(tc.Id));
    }

    [Fact]
    public async Task AddCommentAsync_stores_a_trimmed_comment_with_author_name()
    {
        var sut = CreateSut();
        var tc = await sut.CreateAsync(scopeId, Input(), "user-1");

        await sut.AddCommentAsync(tc.Id, "user-1", "  fails on Safari  ");

        var comment = Assert.Single(await sut.ListCommentsAsync(tc.Id));
        Assert.Equal("fails on Safari", comment.Body);
        Assert.Equal("QA One", comment.AuthorName);
        Assert.Equal("user-1", comment.AuthorId);
        Assert.Equal(time.GetUtcNow(), comment.CreatedUtc);
    }

    [Fact]
    public async Task ListCommentsAsync_orders_by_created()
    {
        var sut = CreateSut();
        var tc = await sut.CreateAsync(scopeId, Input(), "user-1");
        await sut.AddCommentAsync(tc.Id, "user-1", "first");
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.AddCommentAsync(tc.Id, "user-1", "second");

        var bodies = (await sut.ListCommentsAsync(tc.Id)).Select(c => c.Body);

        Assert.Equal(["first", "second"], bodies);
    }

    [Fact]
    public async Task DeleteCommentAsync_removes_only_that_comment()
    {
        var sut = CreateSut();
        var tc = await sut.CreateAsync(scopeId, Input(), "user-1");
        var keepId = await sut.AddCommentAsync(tc.Id, "user-1", "keep");
        var dropId = await sut.AddCommentAsync(tc.Id, "user-1", "drop");

        await sut.DeleteCommentAsync(dropId);

        var remaining = await sut.ListCommentsAsync(tc.Id);
        Assert.Equal(keepId, Assert.Single(remaining).Id);
    }

    [Fact]
    public async Task DeleteAsync_cascades_to_comments()
    {
        var sut = CreateSut();
        var tc = await sut.CreateAsync(scopeId, Input(), "user-1");
        await sut.AddCommentAsync(tc.Id, "user-1", "note");

        await sut.DeleteAsync(tc.Id);

        await using var db = factory.CreateDbContext();
        Assert.Empty(db.TestCaseComments);
    }

    public void Dispose() => connection.Dispose();
}
