using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Defects;

public sealed class DefectServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-03T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly AttachmentService attachments;
    private readonly ProjectService projects;
    private readonly NotificationService notifications;
    private readonly Guid projectId;
    private readonly Guid scopeId;
    private readonly Guid testCaseId;
    private readonly Guid testCaseId2;

    public DefectServiceTests()
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
        projects = new ProjectService(factory, time, attachments);
        notifications = new NotificationService(factory, time);
        projectId = projects.CreateAsync("Proj", null, [], "user-1").GetAwaiter().GetResult().Id;
        var scopes = new TestScopeService(factory, time, projects, attachments);
        scopeId = scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1").GetAwaiter().GetResult().Id;
        var cases = new TestCaseService(factory, time, attachments);
        testCaseId = cases.CreateAsync(scopeId, new TestCaseInput("A scenario", null), "user-1")
            .GetAwaiter().GetResult().Id;
        testCaseId2 = cases.CreateAsync(scopeId, new TestCaseInput("Another scenario", null), "user-1")
            .GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private DefectService CreateSut() => new(factory, time, projects, attachments, notifications);

    private static DefectInput Input(
        string summary = "Modal never opens",
        string? repro = null,
        string? expected = null,
        string? actual = null,
        DefectSeverity severity = DefectSeverity.Medium,
        string? assignee = null) =>
        new(summary, repro, expected, actual, severity, assignee);

    [Fact]
    public async Task CreateAsync_assigns_sequential_numbers_and_trims()
    {
        var sut = CreateSut();

        var first = await sut.CreateAsync(projectId, Input("  first  ", repro: "  a\nb  "), "user-1");
        var second = await sut.CreateAsync(projectId, Input("second"), "user-1");

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.Equal("first", first.Summary);
        Assert.Equal("a\nb", first.ReproSteps);
        Assert.Equal(DefectStatus.NotFixed, first.Status);
        Assert.Equal("user-1", first.CreatedById);
        Assert.Equal(time.GetUtcNow(), first.CreatedUtc);
    }

    [Fact]
    public async Task CreateAsync_flips_project_to_in_flight()
    {
        // A fresh project with no scope/case.
        var freshId = (await projects.CreateAsync("Fresh", null, [], "user-1")).Id;
        var sut = CreateSut();

        await sut.CreateAsync(freshId, Input(), "user-1");

        var project = await projects.GetAsync(freshId);
        Assert.Equal(ProjectStatus.InFlight, project!.Status);
    }

    [Fact]
    public async Task UpdateAsync_updates_fields_and_leaves_status()
    {
        var sut = CreateSut();
        var defect = await sut.CreateAsync(projectId, Input(), "user-1");
        await sut.SetStatusAsync(defect.Id, DefectStatus.Fixing);

        await sut.UpdateAsync(defect.Id, Input("Updated summary", repro: "new steps", assignee: "user-1"));

        var reloaded = await sut.GetAsync(defect.Id);
        Assert.Equal("Updated summary", reloaded!.Summary);
        Assert.Equal("new steps", reloaded.ReproSteps);
        Assert.Equal("user-1", reloaded.AssignedToId);
        Assert.Equal(DefectStatus.Fixing, reloaded.Status);
    }

    [Fact]
    public async Task SetStatusAsync_updates_status_and_timestamp()
    {
        var sut = CreateSut();
        var defect = await sut.CreateAsync(projectId, Input(), "user-1");

        time.Advance(TimeSpan.FromMinutes(30));
        await sut.SetStatusAsync(defect.Id, DefectStatus.Fixed);

        var reloaded = await sut.GetAsync(defect.Id);
        Assert.Equal(DefectStatus.Fixed, reloaded!.Status);
        Assert.Equal(time.GetUtcNow(), reloaded.UpdatedUtc);
    }

    [Fact]
    public async Task SetSeverityAsync_updates_severity_and_timestamp()
    {
        var sut = CreateSut();
        var defect = await sut.CreateAsync(projectId, Input(severity: DefectSeverity.Medium), "user-1");

        time.Advance(TimeSpan.FromMinutes(30));
        await sut.SetSeverityAsync(defect.Id, DefectSeverity.Critical);

        var reloaded = await sut.GetAsync(defect.Id);
        Assert.Equal(DefectSeverity.Critical, reloaded!.Severity);
        Assert.Equal(time.GetUtcNow(), reloaded.UpdatedUtc);
    }

    [Fact]
    public async Task Link_supports_multiple_test_cases_and_is_idempotent()
    {
        var sut = CreateSut();
        var defect = await sut.CreateAsync(projectId, Input(), "user-1");

        await sut.LinkTestCaseAsync(defect.Id, testCaseId);
        await sut.LinkTestCaseAsync(defect.Id, testCaseId2);
        await sut.LinkTestCaseAsync(defect.Id, testCaseId); // no-op

        var linked = (await sut.GetAsync(defect.Id))!.TestCases.Select(tc => tc.Id).ToHashSet();
        Assert.Equal(new[] { testCaseId, testCaseId2 }.ToHashSet(), linked);
    }

    [Fact]
    public async Task UnlinkTestCaseAsync_removes_only_that_link()
    {
        var sut = CreateSut();
        var defect = await sut.CreateAsync(projectId, Input(), "user-1");
        await sut.LinkTestCaseAsync(defect.Id, testCaseId);
        await sut.LinkTestCaseAsync(defect.Id, testCaseId2);

        await sut.UnlinkTestCaseAsync(defect.Id, testCaseId);

        var remaining = (await sut.GetAsync(defect.Id))!.TestCases;
        Assert.Equal(testCaseId2, Assert.Single(remaining).Id);
    }

    [Fact]
    public async Task Deleting_a_test_case_drops_the_link_but_keeps_the_defect()
    {
        var sut = CreateSut();
        var cases = new TestCaseService(factory, time, attachments);
        var defect = await sut.CreateAsync(projectId, Input(), "user-1");
        await sut.LinkTestCaseAsync(defect.Id, testCaseId);

        await cases.DeleteAsync(testCaseId);

        var reloaded = await sut.GetAsync(defect.Id);
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded!.TestCases);
    }

    [Fact]
    public async Task ListForTestCaseAsync_returns_only_linked_defects_ordered_by_number()
    {
        var sut = CreateSut();
        var linked1 = await sut.CreateAsync(projectId, Input("one"), "user-1");
        await sut.CreateAsync(projectId, Input("two"), "user-1");
        var linked2 = await sut.CreateAsync(projectId, Input("three"), "user-1");
        await sut.LinkTestCaseAsync(linked1.Id, testCaseId);
        await sut.LinkTestCaseAsync(linked2.Id, testCaseId);
        await sut.LinkTestCaseAsync(linked2.Id, testCaseId2); // extra link, still listed once

        var list = await sut.ListForTestCaseAsync(testCaseId);

        Assert.Equal([linked1.Id, linked2.Id], list.Select(d => d.Id));
    }

    [Fact]
    public async Task ListForProjectAsync_orders_by_severity_then_number()
    {
        var sut = CreateSut();
        // Numbers 1..7; 1,2,3,7 High and 4,5,6 Low.
        var d1 = await sut.CreateAsync(projectId, Input("1", severity: DefectSeverity.High), "user-1");
        var d2 = await sut.CreateAsync(projectId, Input("2", severity: DefectSeverity.High), "user-1");
        var d3 = await sut.CreateAsync(projectId, Input("3", severity: DefectSeverity.High), "user-1");
        var d4 = await sut.CreateAsync(projectId, Input("4", severity: DefectSeverity.Low), "user-1");
        var d5 = await sut.CreateAsync(projectId, Input("5", severity: DefectSeverity.Low), "user-1");
        var d6 = await sut.CreateAsync(projectId, Input("6", severity: DefectSeverity.Low), "user-1");
        var d7 = await sut.CreateAsync(projectId, Input("7", severity: DefectSeverity.High), "user-1");

        var list = await sut.ListForProjectAsync(projectId);

        Assert.Equal(
            [d1.Id, d2.Id, d3.Id, d7.Id, d4.Id, d5.Id, d6.Id],
            list.Select(d => d.Id));
    }

    [Fact]
    public async Task CreateAsync_persists_severity()
    {
        var sut = CreateSut();

        var defect = await sut.CreateAsync(projectId, Input(severity: DefectSeverity.Critical), "user-1");

        Assert.Equal(DefectSeverity.Critical, (await sut.GetAsync(defect.Id))!.Severity);
    }

    [Fact]
    public async Task SetAssigneeAsync_sets_and_clears_the_assignee()
    {
        var sut = CreateSut();
        var defect = await sut.CreateAsync(projectId, Input(), "user-1");

        await sut.SetAssigneeAsync(defect.Id, "user-1");
        Assert.Equal("user-1", (await sut.GetAsync(defect.Id))!.AssignedToId);

        await sut.SetAssigneeAsync(defect.Id, "  ");
        Assert.Null((await sut.GetAsync(defect.Id))!.AssignedToId);
    }

    [Fact]
    public async Task DeleteAsync_cascades_to_comments()
    {
        var sut = CreateSut();
        var defect = await sut.CreateAsync(projectId, Input(), "user-1");
        await sut.AddCommentAsync(defect.Id, "user-1", "a comment");

        await sut.DeleteAsync(defect.Id);

        await using var db = factory.CreateDbContext();
        Assert.Empty(db.DefectComments);
    }

    [Fact]
    public async Task Comments_add_list_order_and_delete()
    {
        var sut = CreateSut();
        var defect = await sut.CreateAsync(projectId, Input(), "user-1");

        await sut.AddCommentAsync(defect.Id, "user-1", "  first  ");
        time.Advance(TimeSpan.FromMinutes(1));
        var dropId = await sut.AddCommentAsync(defect.Id, "user-1", "second");

        var comments = await sut.ListCommentsAsync(defect.Id);
        Assert.Equal(["first", "second"], comments.Select(c => c.Body));
        Assert.Equal("QA One", comments[0].AuthorName);

        await sut.DeleteCommentAsync(dropId);
        Assert.Equal("first", Assert.Single(await sut.ListCommentsAsync(defect.Id)).Body);
    }

    [Fact]
    public async Task SummariseProjectAsync_total_excludes_not_a_defect()
    {
        var sut = CreateSut();
        var a = await sut.CreateAsync(projectId, Input("a"), "user-1");
        var b = await sut.CreateAsync(projectId, Input("b"), "user-1");
        var c = await sut.CreateAsync(projectId, Input("c"), "user-1");
        await sut.SetStatusAsync(a.Id, DefectStatus.Fixed);
        await sut.SetStatusAsync(b.Id, DefectStatus.NotADefect); // dismissed — shouldn't count
        await sut.SetStatusAsync(c.Id, DefectStatus.Fixing);

        var summary = await sut.SummariseProjectAsync(projectId);

        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Open);
    }

    public void Dispose() => connection.Dispose();
}
