using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Board;
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
    [InlineData("the whole of #s-2 (#S-3)", "S-2,S-3")]
    [InlineData("abc#D-1 x#D-2", "")]
    [InlineData("#D-1x ##D-2 #D- #DD-1 &#D-1 #X-1 #S-", "")]
    [InlineData("#D-0", "")]
    [InlineData("board: #ST-4, #bg-2 and #Tk-9", "ST-4,BG-2,TK-9")]
    [InlineData("#STX-1 #SB-1 #ST- x#ST-1", "")]
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
    public async Task Scopes_link_to_their_page_with_their_type_and_size()
    {
        var (projectId, _, _) = await SeedAsync();
        await SeedAsync("Other"); // has its own S-1

        var map = await references.ResolveAsync(projectId, ["Run all of #S-1 again, not #S-7"]);

        var scope = map.Find("s-1");
        Assert.NotNull(scope);
        Assert.Matches($"^projects/{projectId}/scopes/[0-9a-f-]{{36}}$", scope.Href);
        Assert.Equal("S-1 Auth · Functional · 1 test case", scope.Title);
        Assert.Null(map.Find("S-7"));
    }

    [Fact]
    public async Task Board_items_resolve_and_list_only_while_the_board_is_on()
    {
        var (projectId, _, _) = await SeedAsync();
        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var board = new BoardService(factory, new BoardProviders(new LocalBoardProvider(factory, time)), attachments,
            new NotificationService(factory, time), time);
        await board.SetModeAsync(projectId, BoardMode.Local);
        var story = await board.CreateAsync(projectId, BoardItemType.Story, new BoardItemInput("Saved cards", null, null), "qa");
        await board.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput("Add table", null, null), "qa", story.Id);

        var map = await references.ResolveAsync(projectId, ["Part of #ST-1, see #TK-1 (not #BG-1)"]);
        Assert.Equal($"projects/{projectId}/board/{story.Id}", map.Find("st-1")?.Href);
        Assert.Equal("ST-1 Saved cards · New", map.Find("ST-1")?.Title);
        Assert.Equal("TK-1 Add table · Active", map.Find("TK-1")?.Title);
        Assert.Null(map.Find("BG-1"));

        var options = await references.ListAsync(projectId);
        Assert.Contains(options, o => o is { Ref: "ST-1", Kind: "story", Status: "New" });
        Assert.Contains(options, o => o is { Ref: "TK-1", Kind: "task", Status: "Active" });

        await board.SetModeAsync(projectId, BoardMode.Off);
        Assert.Null((await references.ResolveAsync(projectId, ["#ST-1"])).Find("ST-1"));
        Assert.DoesNotContain(await references.ListAsync(projectId), o => o.Ref == "ST-1");
    }

    [Fact]
    public async Task Resolve_with_no_references_does_not_query()
    {
        var map = await references.ResolveAsync(Guid.NewGuid(), ["nothing to see", null]);

        Assert.Same(CommentReferenceMap.Empty, map);
    }

    [Fact]
    public async Task The_picker_lists_defects_newest_first_then_scopes_then_test_cases()
    {
        var (projectId, _, _) = await SeedAsync();
        await defects.CreateAsync(projectId, new DefectInput("Logout broken", null, null, null, null), "qa");
        await scopes.CreateAsync(projectId, TestCaseKind.NonFunctional, "Performance", "qa");

        var options = await references.ListAsync(projectId);

        Assert.Equal(["D-2", "D-1", "S-1", "S-2", "T-1"], options.Select(o => o.Ref));
        Assert.Equal(new ReferenceOption("S-1", "scope", "Auth", "1 test case"), options[2]);
        Assert.Equal(new ReferenceOption("S-2", "scope", "Performance", "0 test cases"), options[3]);
        Assert.Equal(new ReferenceOption("T-1", "test case", "Signs in", "Not run"), options[4]);
    }
}
