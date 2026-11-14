using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

/// <summary>The execution history behind the test case timeline.</summary>
public sealed class TestCaseHistoryTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-11-10T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly AttachmentService attachments;
    private readonly TestCaseService sut;
    private readonly Guid scopeId;
    private readonly Guid siblingScopeId;

    public TestCaseHistoryTests()
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
            db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "qa@test.local", FullName = "QA One" });
            db.Users.Add(new ApplicationUser { Id = "user-2", UserName = "qa2@test.local", FullName = "QA Two" });
            db.SaveChanges();
        }

        attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        var projects = new ProjectService(factory, time, attachments, notifications);
        var projectId = projects.CreateAsync("Proj", null, null, [], "user-1").GetAwaiter().GetResult().Id;
        var scopes = new TestScopeService(factory, time, projects, attachments);
        scopeId = scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1").GetAwaiter().GetResult().Id;
        siblingScopeId = scopes.CreateAsync(projectId, TestCaseKind.Functional, "Checkout", "user-1").GetAwaiter().GetResult().Id;
        sut = new TestCaseService(factory, time, attachments, notifications);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private Task<TestCase> CreateCaseAsync(string scenario = "Signs in", string? steps = "Open the app") =>
        sut.CreateAsync(scopeId, new TestCaseInput(scenario, steps), "user-1");

    [Fact]
    public async Task Creating_a_case_records_who_created_it()
    {
        var tc = await CreateCaseAsync();

        var entry = Assert.Single(await sut.ListHistoryAsync(tc.Id));
        Assert.Equal(TestCaseEventKind.Created, entry.Kind);
        Assert.Equal("user-1", entry.ActorId);
        Assert.Equal("QA One", entry.ActorName);
        Assert.Equal(time.GetUtcNow(), entry.OccurredUtc);
    }

    [Fact]
    public async Task Result_changes_record_from_and_to_newest_first()
    {
        var tc = await CreateCaseAsync();

        time.Advance(TimeSpan.FromMinutes(5));
        await sut.SetResultAsync(tc.Id, TestResult.Blocked, "user-2");
        time.Advance(TimeSpan.FromMinutes(5));
        await sut.SetResultAsync(tc.Id, TestResult.Failed, "user-1");
        time.Advance(TimeSpan.FromMinutes(5));
        await sut.SetResultAsync(tc.Id, TestResult.Passed, "user-2");

        var history = await sut.ListHistoryAsync(tc.Id);

        Assert.Equal(
            [TestCaseEventKind.ResultChanged, TestCaseEventKind.ResultChanged, TestCaseEventKind.ResultChanged, TestCaseEventKind.Created],
            history.Select(e => e.Kind));
        Assert.Equal((TestResult.Failed, TestResult.Passed, "QA Two"), (history[0].FromResult!.Value, history[0].ToResult!.Value, history[0].ActorName));
        Assert.Equal((TestResult.Blocked, TestResult.Failed, "QA One"), (history[1].FromResult!.Value, history[1].ToResult!.Value, history[1].ActorName));
        Assert.Equal((TestResult.NotRun, TestResult.Blocked), (history[2].FromResult!.Value, history[2].ToResult!.Value));
    }

    [Fact]
    public async Task Setting_the_same_result_again_records_nothing()
    {
        var tc = await CreateCaseAsync();
        await sut.SetResultAsync(tc.Id, TestResult.Passed, "user-1");

        await sut.SetResultAsync(tc.Id, TestResult.Passed, "user-1");

        Assert.Single(await sut.ListHistoryAsync(tc.Id), e => e.Kind == TestCaseEventKind.ResultChanged);
    }

    [Theory]
    [InlineData("Signs in again", "Open the app", TestCaseHistory.EditedScenario)]
    [InlineData("Signs in", "Open the app\nClick sign in", TestCaseHistory.EditedSteps)]
    [InlineData("Signs in again", null, TestCaseHistory.EditedScenarioAndSteps)]
    public async Task Editing_records_what_changed(string scenario, string? steps, string expected)
    {
        var tc = await CreateCaseAsync();

        await sut.UpdateAsync(tc.Id, new TestCaseInput(scenario, steps), "user-2");

        var entry = (await sut.ListHistoryAsync(tc.Id))[0];
        Assert.Equal(TestCaseEventKind.Edited, entry.Kind);
        Assert.Equal(expected, entry.ToValue);
        Assert.Equal("QA Two", entry.ActorName);
    }

    [Fact]
    public async Task Saving_without_changes_records_nothing()
    {
        var tc = await CreateCaseAsync();

        // Whitespace is trimmed on save, so this is the same content.
        await sut.UpdateAsync(tc.Id, new TestCaseInput("  Signs in ", "Open the app  "), "user-1");

        Assert.Single(await sut.ListHistoryAsync(tc.Id));
    }

    [Fact]
    public async Task Moving_records_the_scope_names()
    {
        var tc = await CreateCaseAsync();

        await sut.MoveAsync(tc.Id, siblingScopeId, "user-1");

        var entry = (await sut.ListHistoryAsync(tc.Id))[0];
        Assert.Equal(TestCaseEventKind.Moved, entry.Kind);
        Assert.Equal(("S-1 Auth", "S-2 Checkout"), (entry.FromValue, entry.ToValue));
    }

    [Fact]
    public async Task Adding_and_removing_a_file_is_recorded()
    {
        var tc = await CreateCaseAsync();

        time.Advance(TimeSpan.FromMinutes(1));
        var file = await attachments.UploadAsync(AttachmentOwner.TestCase, tc.Id,
            new MemoryStream(Encoding.UTF8.GetBytes("hi")), "evidence.txt", "text/plain", 2, null, "user-2");
        time.Advance(TimeSpan.FromMinutes(1));
        await attachments.DeleteAsync(file.Id, "user-1");

        var history = await sut.ListHistoryAsync(tc.Id);
        Assert.Equal(TestCaseEventKind.FileRemoved, history[0].Kind);
        Assert.Equal(("evidence.txt", "QA One"), (history[0].FromValue, history[0].ActorName));
        Assert.Equal(TestCaseEventKind.FileAdded, history[1].Kind);
        Assert.Equal(("evidence.txt", "QA Two"), (history[1].ToValue, history[1].ActorName));
    }

    [Fact]
    public async Task Files_on_other_owners_do_not_touch_test_case_history()
    {
        var tc = await CreateCaseAsync();
        await sut.AddCommentAsync(tc.Id, "user-1", "note");
        await using (var db = factory.CreateDbContext())
        {
            var commentId = db.TestCaseComments.Single().Id;
            await attachments.UploadAsync(AttachmentOwner.TestCaseComment, commentId,
                new MemoryStream([1]), "comment.png", "image/png", 1, null, "user-1");
        }

        Assert.Single(await sut.ListHistoryAsync(tc.Id));
    }

    [Fact]
    public async Task History_survives_its_actor_being_deleted()
    {
        var tc = await CreateCaseAsync();
        await sut.SetResultAsync(tc.Id, TestResult.Failed, "user-2");
        // Resetting clears "Tested by", so user-2 is left only in the history.
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.SetResultAsync(tc.Id, TestResult.NotRun, "user-1");

        await using (var db = factory.CreateDbContext())
        {
            // user-2 has no authored content, only history — which mustn't block the delete.
            db.Users.Remove(db.Users.Single(u => u.Id == "user-2"));
            await db.SaveChangesAsync();
        }

        var entry = (await sut.ListHistoryAsync(tc.Id))[1];
        Assert.Equal(TestResult.Failed, entry.ToResult);
        Assert.Equal(TestCaseEventKind.ResultChanged, entry.Kind);
        Assert.Null(entry.ActorId);
        Assert.Equal("Unknown", entry.ActorName);
    }

    [Fact]
    public async Task Deleting_the_case_deletes_its_history()
    {
        var tc = await CreateCaseAsync();
        await sut.SetResultAsync(tc.Id, TestResult.Passed, "user-1");

        await sut.DeleteAsync(tc.Id);

        await using var db = factory.CreateDbContext();
        Assert.Empty(db.TestCaseEvents);
    }

    public void Dispose() => connection.Dispose();
}
