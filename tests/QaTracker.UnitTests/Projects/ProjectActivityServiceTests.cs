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

namespace QaTracker.UnitTests.Projects;

/// <summary>The project's own history and the merged activity feed built on it.</summary>
public sealed class ProjectActivityServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-11-12T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly ProjectActivityService sut;
    private readonly Guid projectId;

    public ProjectActivityServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        factory = new TestDbContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "qa", UserName = "qa@test.local", FullName = "QA One" });
            db.Users.Add(new ApplicationUser { Id = "dev", UserName = "dev@test.local", FullName = "Dev One" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        projects = new ProjectService(factory, time, attachments, notifications);
        scopes = new TestScopeService(factory, time, projects, attachments);
        cases = new TestCaseService(factory, time, attachments, notifications);
        defects = new DefectService(factory, time, projects, attachments, notifications);
        sut = new ProjectActivityService(factory);
        projectId = projects.CreateAsync("Proj", null, null, [], "qa").GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    private void Tick() => time.Advance(TimeSpan.FromMinutes(1));

    private async Task<IReadOnlyList<ProjectActivityItem>> FeedAsync(ActivityFilter filter = ActivityFilter.All) =>
        (await sut.ListAsync(projectId, filter, size: 100)).Items;

    private static string Describe(ProjectActivityItem item) => item switch
    {
        ProjectActivity p => $"Project.{p.Event.Kind}",
        TestCaseActivity t => $"TestCase.{t.Event.Kind}",
        DefectActivity d => $"Defect.{d.Event.Kind}",
        _ => "?",
    };

    private async Task<Guid> ScopeAsync(string name = "Auth")
    {
        Tick();
        return (await scopes.CreateAsync(projectId, TestCaseKind.Functional, name, "qa")).Id;
    }

    [Fact]
    public async Task Creating_a_project_records_it()
    {
        var entry = Assert.IsType<ProjectActivity>(Assert.Single(await FeedAsync()));

        Assert.Equal(ProjectEventKind.Created, entry.Event.Kind);
        Assert.Equal("QA One", entry.Event.ActorName);
    }

    [Fact]
    public async Task Feed_merges_headline_test_case_and_defect_moments_newest_first()
    {
        var scopeId = await ScopeAsync();
        Tick();
        var tc = await cases.CreateAsync(scopeId, new TestCaseInput("Signs in", "Open"), "qa");
        Tick();
        await cases.UpdateAsync(tc.Id, new TestCaseInput("Signs in", "Open the app"), "qa"); // edit: not in the feed
        Tick();
        await cases.SetResultAsync(tc.Id, TestResult.Failed, "qa");
        Tick();
        var defect = await defects.CreateAsync(projectId, new DefectInput("Login broken", null, null, null, null), "qa");
        Tick();
        await defects.SetSeverityAsync(defect.Id, DefectSeverity.High, "qa"); // severity: not in the feed
        Tick();
        await defects.StartFixingAsync(defect.Id, "dev");

        var feed = await FeedAsync();

        Assert.Equal(
            [
                "Defect.StatusChanged", "Defect.AssigneeChanged", // one action, status reads first
                "Defect.Created",
                "TestCase.ResultChanged",
                "TestCase.Created",
                "Project.ScopeCreated",
                "Project.StatusChanged", // first scope moved the project In flight
                "Project.Created",
            ],
            feed.Select(Describe));

        var result = Assert.IsType<TestCaseActivity>(feed[3]);
        Assert.Equal((tc.Id, "Signs in"), (result.TestCaseId, result.Scenario));
        var raised = Assert.IsType<DefectActivity>(feed[2]);
        Assert.Equal((defect.Id, 1, "Login broken"), (raised.DefectId, raised.Number, raised.Summary));

        var inFlight = Assert.IsType<ProjectActivity>(feed[6]);
        Assert.Equal((ProjectStatus.NotStarted, ProjectStatus.InFlight, "QA One"),
            (inFlight.Event.FromStatus, inFlight.Event.ToStatus, inFlight.Event.ActorName));
    }

    [Fact]
    public async Task Feed_only_shows_this_projects_activity()
    {
        var other = await projects.CreateAsync("Other", null, null, [], "qa");
        await defects.CreateAsync(other.Id, new DefectInput("Elsewhere", null, null, null, null), "qa");

        Assert.Equal(["Project.Created"], (await FeedAsync()).Select(Describe));
    }

    [Fact]
    public async Task Filters_pick_one_kind_of_activity()
    {
        var scopeId = await ScopeAsync();
        Tick();
        await cases.CreateAsync(scopeId, new TestCaseInput("Signs in", null), "qa");
        Tick();
        await defects.CreateAsync(projectId, new DefectInput("Bug", null, null, null, null), "qa");

        Assert.Equal(["TestCase.Created"], (await FeedAsync(ActivityFilter.TestCases)).Select(Describe));
        Assert.Equal(["Defect.Created"], (await FeedAsync(ActivityFilter.Defects)).Select(Describe));
        Assert.All(await FeedAsync(ActivityFilter.Project), i => Assert.IsType<ProjectActivity>(i));
    }

    [Fact]
    public async Task Status_changes_are_recorded_only_when_the_status_changes()
    {
        Tick();
        await projects.SetStatusAsync(projectId, ProjectStatus.Complete, "dev");
        Tick();
        await projects.SetStatusAsync(projectId, ProjectStatus.Complete, "dev");

        var feed = await FeedAsync();

        Assert.Equal(["Project.StatusChanged", "Project.Created"], feed.Select(Describe));
        var entry = Assert.IsType<ProjectActivity>(feed[0]).Event;
        Assert.Equal((ProjectStatus.NotStarted, ProjectStatus.Complete, "Dev One"), (entry.FromStatus, entry.ToStatus, entry.ActorName));
    }

    [Fact]
    public async Task Team_changes_record_the_members_names()
    {
        Tick();
        await projects.AddMembersAsync(projectId, ["dev", "qa"], "qa");
        Tick();
        await projects.RemoveMemberAsync(projectId, "dev", "qa");

        var feed = (await FeedAsync(ActivityFilter.Project)).Cast<ProjectActivity>().Select(p => p.Event).ToList();

        Assert.Equal((ProjectEventKind.MemberRemoved, "Dev One"), (feed[0].Kind, feed[0].FromValue));
        Assert.Equal(
            ["Dev One", "QA One"],
            feed.Where(e => e.Kind == ProjectEventKind.MemberAdded).Select(e => e.ToValue!).Order());
    }

    [Fact]
    public async Task Edits_report_what_changed_and_record_one_entry()
    {
        Tick();
        var changed = new List<string>();
        changed.AddRange(await projects.UpdateAsync(projectId, "Proj", "A brief", null, [new ProjectLinkInput("Spec", "https://example.com")]));
        changed.AddRange(await projects.UpdateScheduleAsync(projectId, ProjectSchedule.Empty));
        changed.AddRange(await projects.UpdateDefaultDefectTemplateAsync(projectId, null));
        await projects.RecordEditedAsync(projectId, changed, "qa");

        Assert.Equal(["brief", "links"], changed);
        var entry = Assert.IsType<ProjectActivity>((await FeedAsync())[0]).Event;
        Assert.Equal((ProjectEventKind.Edited, "brief and links"), (entry.Kind, entry.ToValue));

        // Saving the same values again changes nothing, so nothing is recorded.
        Tick();
        var again = await projects.UpdateAsync(projectId, "Proj", "A brief", null, [new ProjectLinkInput("Spec", "https://example.com")]);
        await projects.RecordEditedAsync(projectId, again, "qa");
        Assert.Empty(again);
        Assert.Equal(2, (await FeedAsync()).Count);
    }

    [Fact]
    public async Task Deleting_items_is_recorded_on_the_project_and_their_history_goes()
    {
        var scopeId = await ScopeAsync("Auth");
        var tc = await cases.CreateAsync(scopeId, new TestCaseInput("Signs in", null), "qa");
        await cases.CreateAsync(scopeId, new TestCaseInput("Signs out", null), "qa");
        var defect = await defects.CreateAsync(projectId, new DefectInput("Login broken", null, null, null, null), "qa");

        Tick();
        await cases.DeleteAsync(tc.Id, "qa");
        Tick();
        await defects.DeleteAsync(defect.Id, "dev");
        Tick();
        await scopes.DeleteAsync(scopeId, "qa");

        var feed = await FeedAsync();
        Assert.DoesNotContain(feed, i => i is TestCaseActivity or DefectActivity);

        var events = feed.Cast<ProjectActivity>().Select(p => p.Event).ToList();
        Assert.Equal((ProjectEventKind.ScopeDeleted, "S-1 Auth", "1"), (events[0].Kind, events[0].FromValue, events[0].ToValue));
        Assert.Equal((ProjectEventKind.DefectDeleted, "D-1 Login broken", "Dev One"), (events[1].Kind, events[1].FromValue, events[1].ActorName));
        Assert.Equal((ProjectEventKind.TestCaseDeleted, "T-1 Signs in"), (events[2].Kind, events[2].FromValue));
    }

    [Fact]
    public async Task Scope_entries_link_only_while_the_scope_exists()
    {
        var kept = await ScopeAsync("Auth");
        var gone = await ScopeAsync("Checkout");
        Tick();
        await scopes.UpdateAsync(kept, TestCaseKind.Functional, "Sign-in", "qa");
        await scopes.DeleteAsync(gone, "qa");

        var events = (await FeedAsync(ActivityFilter.Project)).Cast<ProjectActivity>().Select(p => p.Event).ToList();

        var renamed = Assert.Single(events, e => e.Kind == ProjectEventKind.ScopeRenamed);
        Assert.Equal(("Auth", "Sign-in", true), (renamed.FromValue, renamed.ToValue, renamed.SubjectExists));
        Assert.True(Assert.Single(events, e => e.Kind == ProjectEventKind.ScopeCreated && e.SubjectId == kept).SubjectExists);
        Assert.False(Assert.Single(events, e => e.Kind == ProjectEventKind.ScopeCreated && e.SubjectId == gone).SubjectExists);
    }

    [Fact]
    public async Task An_import_is_one_entry_rather_than_one_per_case()
    {
        Tick();
        var import = new TestCaseImportService(scopes, cases, defects, projects);
        var parsed = TestCaseImportParser.Parse(
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n"
            + "Auth,Functional,Signs in,,\n"
            + "Auth,Functional,Signs out,,\n");

        await import.ApplyAsync(projectId, parsed, "qa");

        var feed = await FeedAsync();
        Assert.DoesNotContain(feed, i => i is TestCaseActivity);
        var imported = Assert.Single(feed.OfType<ProjectActivity>(), p => p.Event.Kind == ProjectEventKind.TestCasesImported);
        Assert.Equal("2 new test cases, 1 new scope", imported.Event.ToValue);

        // Each case's own history still says where it came from.
        var caseId = (await scopes.ListForProjectAsync(projectId)).Single().Cases.First().Id;
        Assert.Equal(TestCaseEventKind.Imported, Assert.Single(await cases.ListHistoryAsync(caseId)).Kind);
    }

    [Fact]
    public async Task Pages_follow_the_cursor_without_gaps_or_repeats()
    {
        var scopeId = await ScopeAsync();
        for (var i = 1; i <= 5; i++)
        {
            Tick();
            await cases.CreateAsync(scopeId, new TestCaseInput($"Case {i}", null), "qa");
        }

        var seen = new List<Guid>();
        DateTimeOffset? before = null;
        var pages = 0;
        do
        {
            var page = await sut.ListAsync(projectId, ActivityFilter.All, before, size: 3);
            seen.AddRange(page.Items.Select(i => i.Id));
            before = page.NextBefore;
            pages++;
        }
        while (before is not null && pages < 10);

        var all = (await FeedAsync()).Select(i => i.Id).ToList();
        Assert.Equal(all, seen);
        Assert.Equal(3, pages); // 8 entries: 3 + 3 + 2
    }

    [Fact]
    public async Task A_page_never_splits_entries_recorded_in_the_same_instant()
    {
        var first = await defects.CreateAsync(projectId, new DefectInput("Bug", null, null, null, null), "qa");
        Tick();
        await defects.StartFixingAsync(first.Id, "dev"); // status + assignee, same instant

        // Newest two would be [StatusChanged, AssigneeChanged] at one instant, then Created: a page
        // of one must widen to hold both entries of that instant.
        var page = await sut.ListAsync(projectId, ActivityFilter.Defects, size: 1);
        Assert.Equal(["Defect.StatusChanged", "Defect.AssigneeChanged"], page.Items.Select(Describe));
        Assert.NotNull(page.NextBefore);

        var next = await sut.ListAsync(projectId, ActivityFilter.Defects, page.NextBefore, size: 1);
        Assert.Equal(["Defect.Created"], next.Items.Select(Describe));
        Assert.Null(next.NextBefore);
    }

    [Fact]
    public void Cursor_round_trips_through_a_url()
    {
        var when = DateTimeOffset.Parse("2026-11-12T09:30:15.123456Z", CultureInfo.InvariantCulture);

        Assert.Equal(when, ProjectActivityService.ParseCursor(ProjectActivityService.FormatCursor(when)));
        Assert.Null(ProjectActivityService.ParseCursor("not-a-number"));
        Assert.Null(ProjectActivityService.ParseCursor("-5"));
        Assert.Null(ProjectActivityService.ParseCursor(null));
    }

    [Theory]
    [InlineData("project", ActivityFilter.Project)]
    [InlineData("test-cases", ActivityFilter.TestCases)]
    [InlineData("DEFECTS", ActivityFilter.Defects)]
    [InlineData("nonsense", ActivityFilter.All)]
    [InlineData(null, ActivityFilter.All)]
    public void Filter_values_parse(string? value, ActivityFilter expected) =>
        Assert.Equal(expected, ProjectActivityService.ParseFilter(value));
}
