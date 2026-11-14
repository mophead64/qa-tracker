using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Components.Shared;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Comments;

/// <summary>"#D-3" / "#T-12" in comments: finding them, resolving them per project, and splitting them out as links.</summary>
public sealed class CommentReferenceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-14T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly CommentReferenceService references;

    public CommentReferenceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        factory = new TestDbContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "qa", UserName = "qa@test.local", FullName = "QA One" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        projects = new ProjectService(factory, time, attachments, notifications);
        scopes = new TestScopeService(factory, time, projects, attachments);
        cases = new TestCaseService(factory, time, attachments, notifications);
        defects = new DefectService(factory, time, projects, attachments, notifications);
        references = new CommentReferenceService(factory);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    private async Task<(Guid ProjectId, Guid CaseId, Guid DefectId)> SeedAsync(string name = "Proj")
    {
        var projectId = (await projects.CreateAsync(name, null, null, [], "qa")).Id;
        var scopeId = (await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "qa")).Id;
        var caseId = (await cases.CreateAsync(scopeId, new TestCaseInput("Signs in", "Open"), "qa")).Id;
        var defectId = (await defects.CreateAsync(projectId, new DefectInput("Login broken", null, null, null, null), "qa")).Id;
        return (projectId, caseId, defectId);
    }

    [Theory]
    [InlineData("see #D-3", "D-3")]
    [InlineData("#t-12 first", "T-12")]
    [InlineData("(#D-1)", "D-1")]
    [InlineData("#D-1, #T-2.", "D-1,T-2")]
    [InlineData("abc#D-1 x#D-2", "")]
    [InlineData("#D-1x ##D-2 #D- #DD-1 &#D-1", "")]
    [InlineData("#D-0", "")]
    public void References_are_found_only_as_standalone_ids(string body, string expected)
    {
        var found = CommentReference.Find([body]).Select(r => CommentReference.Key(r.Kind, r.Number));

        Assert.Equal(expected, string.Join(",", found));
    }

    [Fact]
    public void Runs_link_known_references_and_leave_the_rest_alone()
    {
        var map = new CommentReferenceMap([new ResolvedReference("D-3", "projects/p/defects/d", "D-3 Login broken · Fixing")]);
        const string body = "@Ann see #d-3 and #D-4";

        var runs = CommentBody.Runs(body, ["Ann"], map).ToList();

        Assert.Equal(body, string.Concat(runs.Select(r => r.Text)));
        Assert.Equal(["@Ann", " see ", "#d-3", " and #D-4"], runs.Select(r => r.Text));
        Assert.True(runs[0].IsMention);
        Assert.Equal("projects/p/defects/d", runs[2].Reference?.Href);
        Assert.Null(runs[3].Reference);
    }

    [Fact]
    public void Runs_without_references_are_just_the_mention_segments()
    {
        var runs = CommentBody.Runs("hi @Ann #D-1", ["Ann"], CommentReferenceMap.Empty).ToList();

        Assert.Equal(["hi ", "@Ann", " #D-1"], runs.Select(r => r.Text));
    }

    [Fact]
    public async Task Resolve_links_items_in_the_same_project_only()
    {
        var (projectId, caseId, defectId) = await SeedAsync();
        await SeedAsync("Other"); // has its own D-1 / T-1

        var map = await references.ResolveAsync(projectId, ["Broken by #D-1, found in #T-1", "and #D-9"]);

        Assert.Equal(2, map.Count);
        Assert.Equal($"projects/{projectId}/defects/{defectId}", map.Find("D-1")?.Href);
        Assert.Equal("D-1 Login broken · Not fixed", map.Find("d-1")?.Title);
        Assert.Equal($"projects/{projectId}/test-cases/{caseId}", map.Find("T-1")?.Href);
        Assert.Null(map.Find("D-9"));
    }

    [Fact]
    public async Task Resolve_with_no_references_does_not_query()
    {
        var map = await references.ResolveAsync(Guid.NewGuid(), ["nothing to see", null]);

        Assert.Same(CommentReferenceMap.Empty, map);
    }

    [Fact]
    public async Task The_picker_lists_defects_newest_first_then_test_cases()
    {
        var (projectId, _, _) = await SeedAsync();
        await defects.CreateAsync(projectId, new DefectInput("Logout broken", null, null, null, null), "qa");

        var options = await references.ListAsync(projectId);

        Assert.Equal(["D-2", "D-1", "T-1"], options.Select(o => o.Ref));
        Assert.Equal(new ReferenceOption("T-1", "test case", "Signs in", "Not run"), options[2]);
    }
}
