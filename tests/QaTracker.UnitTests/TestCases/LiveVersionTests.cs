using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

/// <summary>The version stamps live pages poll: they move when what the page shows changes, and only then.</summary>
public sealed class LiveVersionTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-12T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly CommentReactionService reactions;
    private readonly Guid projectId;
    private readonly Guid scopeId;
    private readonly Guid caseId;

    public LiveVersionTests()
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
        reactions = new CommentReactionService(factory, time, notifications);
        projectId = projects.CreateAsync("Proj", null, null, [], "qa").GetAwaiter().GetResult().Id;
        scopeId = scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "qa").GetAwaiter().GetResult().Id;
        caseId = cases.CreateAsync(scopeId, new TestCaseInput("Signs in", "Open"), "qa").GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    private async Task<string> DashboardVersionAsync() => (await projects.GetDashboardLiveVersionAsync(projectId))!;

    private async Task<string> CaseVersionAsync() => (await cases.GetLiveVersionAsync(caseId))!;

    private Task<string> PlanVersionAsync() => scopes.GetPlanLiveVersionAsync(projectId);

    /// <summary>Runs <paramref name="change"/> and asserts the stamp moved.</summary>
    private async Task AssertMovesAsync(Func<Task<string>> version, Func<Task> change)
    {
        var before = await version();
        time.Advance(TimeSpan.FromMinutes(1));
        await change();
        Assert.NotEqual(before, await version());
    }

    [Fact]
    public async Task Case_stamp_is_stable_while_nothing_changes()
    {
        var first = await CaseVersionAsync();

        Assert.Equal(first, await CaseVersionAsync());
        Assert.Matches("^[0-9a-f]{16}$", first);
    }

    [Fact]
    public async Task Case_stamp_moves_with_results_edits_comments_reactions_and_defects()
    {
        await AssertMovesAsync(CaseVersionAsync, () => cases.SetResultAsync(caseId, TestResult.Failed, "qa"));
        await AssertMovesAsync(CaseVersionAsync, () => cases.UpdateAsync(caseId, new TestCaseInput("Signs in", "Open the app"), "qa"));

        Guid commentId = default;
        await AssertMovesAsync(CaseVersionAsync, async () => commentId = await cases.AddCommentAsync(caseId, "qa", "Broken on Safari"));
        await AssertMovesAsync(CaseVersionAsync, () => reactions.ReactAsync(CommentKind.TestCase, commentId, "dev", Reaction.Up));
        // Switching a reaction keeps the count the same but still counts as a change.
        await AssertMovesAsync(CaseVersionAsync, () => reactions.ReactAsync(CommentKind.TestCase, commentId, "dev", Reaction.Down));

        var defect = await defects.CreateAsync(projectId, new DefectInput("Login broken", null, null, null, null), "qa");
        await AssertMovesAsync(CaseVersionAsync, () => defects.LinkTestCaseAsync(defect.Id, caseId, "qa"));
        // A linked defect moving on elsewhere shows on this page too.
        await AssertMovesAsync(CaseVersionAsync, () => defects.StartFixingAsync(defect.Id, "dev"));

        await AssertMovesAsync(CaseVersionAsync, () => cases.DeleteCommentAsync(commentId));
    }

    [Fact]
    public async Task Case_stamp_moves_when_another_case_in_the_project_is_run()
    {
        var other = await cases.CreateAsync(scopeId, new TestCaseInput("Signs out", null), "qa");

        // The page's Previous / Next and "cases to action" depend on the rest of the run.
        await AssertMovesAsync(CaseVersionAsync, () => cases.SetResultAsync(other.Id, TestResult.Passed, "qa"));
    }

    [Fact]
    public async Task Case_stamp_is_null_once_the_case_is_gone()
    {
        await cases.DeleteAsync(caseId, "qa");

        Assert.Null(await cases.GetLiveVersionAsync(caseId));
    }

    [Fact]
    public async Task Plan_stamp_moves_with_cases_results_and_scopes()
    {
        Assert.Equal(await PlanVersionAsync(), await PlanVersionAsync());

        await AssertMovesAsync(PlanVersionAsync, () => cases.SetResultAsync(caseId, TestResult.Passed, "qa"));
        await AssertMovesAsync(PlanVersionAsync, () => cases.CreateAsync(scopeId, new TestCaseInput("Signs out", null), "qa"));
        await AssertMovesAsync(PlanVersionAsync, () => scopes.UpdateAsync(scopeId, TestCaseKind.Functional, "Sign-in", "qa"));
        var second = await scopes.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "qa");
        await AssertMovesAsync(PlanVersionAsync, () => scopes.ReorderAsync(projectId, [second.Id, scopeId]));
        await AssertMovesAsync(PlanVersionAsync, () => cases.DeleteAsync(caseId, "qa"));
    }

    [Fact]
    public async Task Defect_stamp_moves_with_the_defect_its_comments_its_cases_the_team_and_the_workflow()
    {
        var defect = await defects.CreateAsync(projectId, new DefectInput("Login broken", null, null, null, null), "qa");
        Task<string> Version() => defects.GetLiveVersionAsync(projectId, defect.Id)!;
        Assert.Equal(await Version(), await Version());

        await AssertMovesAsync(Version, () => defects.SetSeverityAsync(defect.Id, DefectSeverity.High, "qa"));
        await AssertMovesAsync(Version, () => defects.SetAssigneeAsync(defect.Id, "dev", "qa"));
        await AssertMovesAsync(Version, () => defects.StartFixingAsync(defect.Id, "dev"));
        await AssertMovesAsync(Version, () => defects.UpdateAsync(defect.Id,
            new DefectInput("Login broken on Safari", null, null, null, "dev"), "qa"));

        Guid commentId = default;
        await AssertMovesAsync(Version, async () => commentId = await defects.AddCommentAsync(defect.Id, "dev", "Looking"));
        await AssertMovesAsync(Version, () => reactions.ReactAsync(CommentKind.Defect, commentId, "qa", Reaction.Up));

        await AssertMovesAsync(Version, () => defects.LinkTestCaseAsync(defect.Id, caseId, "qa"));
        // A linked case being run shows on the defect page.
        await AssertMovesAsync(Version, () => cases.SetResultAsync(caseId, TestResult.Failed, "qa"));

        // The assignee picker lists the team; Previous / Next follows other defects' workflow.
        await AssertMovesAsync(Version, () => new ProjectService(factory, time,
            new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance),
            new NotificationService(factory, time)).AddMembersAsync(projectId, ["dev"], "qa"));
        var other = await defects.CreateAsync(projectId, new DefectInput("Other", null, null, null, null), "qa");
        await AssertMovesAsync(Version, () => defects.SetStatusAsync(other.Id, DefectStatus.Fixing, "qa"));
    }

    [Fact]
    public async Task Defect_stamp_is_null_outside_its_project_or_once_deleted()
    {
        var defect = await defects.CreateAsync(projectId, new DefectInput("Bug", null, null, null, null), "qa");

        Assert.Null(await defects.GetLiveVersionAsync(Guid.NewGuid(), defect.Id));
        await defects.DeleteAsync(defect.Id, "qa");
        Assert.Null(await defects.GetLiveVersionAsync(projectId, defect.Id));
    }

    [Fact]
    public async Task Defect_list_stamp_moves_when_defects_are_raised_moved_on_or_deleted()
    {
        Task<string> Version() => defects.GetListLiveVersionAsync(projectId);
        Assert.Equal(await Version(), await Version());

        Defect defect = null!;
        await AssertMovesAsync(Version, async () => defect = await defects.CreateAsync(projectId, new DefectInput("Bug", null, null, null, null), "qa"));
        await AssertMovesAsync(Version, () => defects.SetStatusAsync(defect.Id, DefectStatus.ToCheck, "qa"));
        await AssertMovesAsync(Version, () => defects.SetAssigneeAsync(defect.Id, "dev", "qa"));
        await AssertMovesAsync(Version, () => defects.DeleteAsync(defect.Id, "qa"));
    }

    [Fact]
    public async Task Dashboard_stamp_is_stable_while_nothing_changes()
    {
        var first = await DashboardVersionAsync();

        Assert.Equal(first, await DashboardVersionAsync());
    }

    [Fact]
    public async Task Dashboard_stamp_moves_with_the_project_its_team_comments_tests_and_defects()
    {
        await AssertMovesAsync(DashboardVersionAsync, () => projects.SetStatusAsync(projectId, ProjectStatus.InFlight, "qa"));
        await AssertMovesAsync(DashboardVersionAsync, () => projects.AddMembersAsync(projectId, ["dev"], "qa"));
        await AssertMovesAsync(DashboardVersionAsync, () => projects.AddLinkAsync(projectId, "Spec", "https://example.com/spec", "qa"));

        Guid commentId = default;
        await AssertMovesAsync(DashboardVersionAsync, async () => commentId = await projects.AddCommentAsync(projectId, "qa", "Kick-off at 10"));
        await AssertMovesAsync(DashboardVersionAsync, () => reactions.ReactAsync(CommentKind.Project, commentId, "dev", Reaction.Up));
        await AssertMovesAsync(DashboardVersionAsync, () => projects.DeleteCommentAsync(commentId));

        // The summary tallies, Actions needed and Recent activity follow results and the defect workflow.
        await AssertMovesAsync(DashboardVersionAsync, () => cases.SetResultAsync(caseId, TestResult.Failed, "qa"));
        var defect = await defects.CreateAsync(projectId, new DefectInput("Login broken", null, null, null, null), "qa");
        await AssertMovesAsync(DashboardVersionAsync, () => defects.StartFixingAsync(defect.Id, "dev"));
        await AssertMovesAsync(DashboardVersionAsync, () => defects.MarkFixedAsync(defect.Id, "dev"));
    }

    [Fact]
    public async Task Dashboard_stamp_is_null_once_the_project_is_gone()
    {
        await projects.DeleteAsync(projectId);

        Assert.Null(await projects.GetDashboardLiveVersionAsync(projectId));
    }

    [Fact]
    public void Of_is_order_sensitive_and_handles_lists_and_nulls()
    {
        Assert.Equal(LiveVersion.Of("a", 1, null), LiveVersion.Of("a", 1, null));
        Assert.NotEqual(LiveVersion.Of("a", "b"), LiveVersion.Of("b", "a"));
        Assert.NotEqual(LiveVersion.Of(new[] { "a", "b" }), LiveVersion.Of(new[] { "ab" }));
        Assert.NotEqual(LiveVersion.Of((object?)null), LiveVersion.Of(""));
    }

    [Theory]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/test-cases/live", true)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/test-cases/3f2b8c1e-0000-4000-8000-000000000002/live", true)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/test-cases", false)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/test-cases/3f2b8c1e-0000-4000-8000-000000000002", false)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/test-cases/3f2b8c1e-0000-4000-8000-000000000002/history", false)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/defects/live", true)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/defects/3f2b8c1e-0000-4000-8000-000000000002/live", true)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/defects/3f2b8c1e-0000-4000-8000-000000000002", false)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/live", true)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001", false)]
    [InlineData("/projects/3f2b8c1e-0000-4000-8000-000000000001/activity/live", false)]
    [InlineData("/live", false)]
    public void Poll_requests_are_recognised_for_log_and_trace_filtering(string path, bool expected) =>
        Assert.Equal(expected, LiveVersion.IsPollPath(new Microsoft.AspNetCore.Http.PathString(path)));

    [Theory]
    [InlineData(null, 10)]
    [InlineData("30", 30)]
    [InlineData("0", 2)]
    [InlineData("99999", 3600)]
    public void Poll_interval_comes_from_configuration_within_bounds(string? configured, int expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configured is null ? [] : [new("QATRACKER_LIVE_POLL_SECONDS", configured)])
            .Build();

        Assert.Equal(expected, LiveVersion.PollSeconds(config));
    }
}
