using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Admin;

/// <summary>A user's activity across projects, for the System settings user pages.</summary>
public sealed class UserActivityServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-11-14T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly AttachmentService attachments;
    private readonly UserActivityService sut;

    public UserActivityServiceTests()
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

        attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        projects = new ProjectService(factory, time, attachments, notifications);
        scopes = new TestScopeService(factory, time, projects, attachments);
        cases = new TestCaseService(factory, time, attachments, notifications);
        defects = new DefectService(factory, time, projects, attachments, notifications);
        sut = new UserActivityService(factory);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    private void Tick() => time.Advance(TimeSpan.FromMinutes(1));

    private async Task<(Guid ProjectId, Guid CaseId)> ProjectWithCaseAsync(string name, string scenario = "Signs in")
    {
        Tick();
        var project = await projects.CreateAsync(name, null, null, [], "qa");
        Tick();
        var scope = await scopes.CreateAsync(project.Id, TestCaseKind.Functional, "Auth", "qa");
        Tick();
        var tc = await cases.CreateAsync(scope.Id, new TestCaseInput(scenario, null), "qa");
        return (project.Id, tc.Id);
    }

    private static MemoryStream Content() => new([1, 2, 3, 4, 5]);

    [Fact]
    public async Task Counts_testing_defect_work_comments_and_files_per_project_and_overall()
    {
        var (alpha, alphaCase) = await ProjectWithCaseAsync("Alpha");
        var (beta, betaCase) = await ProjectWithCaseAsync("Beta");

        // QA tests: a run started, a fail, a pass on Alpha; a block on Beta.
        Tick(); await cases.SetResultAsync(alphaCase, TestResult.Running, "qa");
        Tick(); await cases.SetResultAsync(alphaCase, TestResult.Failed, "qa");
        Tick(); await cases.SetResultAsync(betaCase, TestResult.Blocked, "qa");

        // QA raises a defect, dev fixes it, QA sends it back, dev fixes again, QA verifies.
        Tick();
        var defect = await defects.CreateAsync(alpha, new DefectInput("Login broken", null, null, null, null), "qa");
        Tick(); await defects.StartFixingAsync(defect.Id, "dev");
        Tick(); await defects.MarkFixedAsync(defect.Id, "dev");
        Tick(); await defects.RejectFixAsync(defect.Id, "qa");
        Tick(); await defects.StartFixingAsync(defect.Id, "dev");
        Tick(); await defects.MarkFixedAsync(defect.Id, "dev");
        Tick(); await defects.VerifyFixedAsync(defect.Id, "qa");
        Tick(); await cases.SetResultAsync(alphaCase, TestResult.Passed, "qa");

        Tick(); await cases.AddCommentAsync(alphaCase, "qa", "Fails on Safari");
        Tick(); var defectComment = await defects.AddCommentAsync(defect.Id, "dev", "Fixed in build 12");
        Tick(); await projects.AddCommentAsync(beta, "qa", "Kick-off notes");
        Tick(); await attachments.UploadAsync(AttachmentOwner.TestCase, alphaCase, Content(), "shot.png", "image/png", 5, null, "qa");
        Tick(); await attachments.UploadAsync(AttachmentOwner.DefectComment, defectComment, Content(), "log.txt", "text/plain", 5, null, "dev");

        var qa = await sut.GetAsync("qa");

        Assert.Equal(
            new UserActivityCounts(ResultsRecorded: 3, CasesTested: 2, DefectsRaised: 1, DefectsFixed: 0, FixesVerified: 1,
                FixesSentBack: 1, Comments: 2, Files: 1, FileBytes: 5, HasActivity: true, LastActiveUtc: time.GetUtcNow().AddMinutes(-1)),
            qa.Totals);
        Assert.Equal(["Alpha", "Beta"], qa.Projects.Select(p => p.Name).Order());
        Assert.Equal(1, qa.ResultTally[TestResult.Running]);
        Assert.Equal(
            [TestResult.Passed, TestResult.Blocked, TestResult.Failed, TestResult.Running],
            qa.TestRuns.Select(r => r.To)); // newest first
        Assert.Equal(
            [DefectWorkKind.Verified, DefectWorkKind.SentBack],
            qa.DefectWork.Select(w => w.Kind));

        var qaOnAlpha = await sut.GetAsync("qa", alpha);
        Assert.Equal((2, 1, 1, 1), (qaOnAlpha.Totals.ResultsRecorded, qaOnAlpha.Totals.CasesTested, qaOnAlpha.Totals.Comments, qaOnAlpha.Totals.Files));
        Assert.All(qaOnAlpha.Timeline, t => Assert.Equal(alpha, t.ProjectId));
        Assert.DoesNotContain(qaOnAlpha.Comments, c => c.Target == UserActivityTarget.Project);

        var dev = await sut.GetAsync("dev");
        Assert.Equal((1, 0, 1, 1), (dev.Totals.DefectsFixed, dev.Totals.ResultsRecorded, dev.Totals.Comments, dev.Totals.Files));
        Assert.Equal([DefectWorkKind.Fixed, DefectWorkKind.Fixed], dev.DefectWork.Select(w => w.Kind));
        var upload = Assert.Single(dev.Uploads);
        Assert.Equal((UserActivityTarget.Defect, defect.Id, true, "log.txt"), (upload.Target, upload.TargetId, upload.OnComment, upload.FileName));
        Assert.Equal(["Alpha"], dev.Projects.Select(p => p.Name));
    }

    [Fact]
    public async Task Timeline_holds_every_history_entry_they_made_plus_their_comments_newest_first()
    {
        var (projectId, caseId) = await ProjectWithCaseAsync("Alpha");
        Tick(); await cases.UpdateAsync(caseId, new TestCaseInput("Signs in", "Open the app"), "qa");
        Tick(); await cases.AddCommentAsync(caseId, "qa", "Looks good");
        Tick(); await cases.SetResultAsync(caseId, TestResult.Passed, "dev"); // someone else

        var report = await sut.GetAsync("qa");

        Assert.Equal(
            ["Comment", "TestCase.Edited", "TestCase.Created"],
            report.Timeline.Take(3).Select(t => t.History switch
            {
                TestCaseActivity a => $"TestCase.{a.Event.Kind}",
                ProjectActivity p => $"Project.{p.Event.Kind}",
                DefectActivity d => $"Defect.{d.Event.Kind}",
                _ => "Comment",
            }));
        Assert.Empty(report.TestRuns);
        Assert.All(report.Timeline.Where(t => t.History is not null), t => Assert.NotEqual("Unknown", ActorName(t.History!)));
    }

    private static string ActorName(ProjectActivityItem item) => item switch
    {
        TestCaseActivity a => a.Event.ActorName,
        ProjectActivity p => p.Event.ActorName,
        DefectActivity d => d.Event.ActorName,
        _ => "",
    };

    [Fact]
    public async Task Fixes_from_before_defect_history_count_from_the_defects_fields()
    {
        var (projectId, _) = await ProjectWithCaseAsync("Alpha");
        var defect = await defects.CreateAsync(projectId, new DefectInput("Old bug", null, null, null, null), "qa");
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Defects.SingleAsync(d => d.Id == defect.Id);
            row.FixedById = "dev";
            row.TestedById = "qa";
            row.Status = DefectStatus.Fixed;
            await db.SaveChangesAsync();
        }

        var dev = await sut.GetAsync("dev");
        var qa = await sut.GetAsync("qa");

        var fixedWork = Assert.Single(dev.DefectWork);
        Assert.Equal((DefectWorkKind.Fixed, (DateTimeOffset?)null, DefectStatus.Fixed), (fixedWork.Kind, fixedWork.OccurredUtc, fixedWork.CurrentStatus));
        Assert.Equal((1, true, (DateTimeOffset?)null), (dev.Totals.DefectsFixed, dev.Totals.HasActivity, dev.Totals.LastActiveUtc));
        Assert.Equal(1, qa.Totals.FixesVerified);
    }

    [Fact]
    public async Task Comments_know_which_page_of_their_thread_they_are_on()
    {
        var (_, caseId) = await ProjectWithCaseAsync("Alpha");
        for (var i = 0; i < 6; i++)
        {
            Tick();
            await cases.AddCommentAsync(caseId, i == 0 ? "dev" : "qa", $"Comment {i}");
        }

        var dev = Assert.Single((await sut.GetAsync("dev")).Comments);
        var qaPages = (await sut.GetAsync("qa")).Comments.Select(c => (c.Body, c.ThreadPage)).OrderBy(c => c.Body);

        Assert.Equal(1, dev.ThreadPage);
        Assert.Equal(
            [("Comment 1", 1), ("Comment 2", 1), ("Comment 3", 1), ("Comment 4", 1), ("Comment 5", 2)],
            qaPages);
    }

    [Fact]
    public async Task Team_projects_with_no_activity_are_listed_as_scopes()
    {
        Tick();
        var project = await projects.CreateAsync("Quiet", null, null, [], "qa");
        Tick();
        await projects.AddMembersAsync(project.Id, ["dev"], "qa");

        var dev = await sut.GetAsync("dev");

        var row = Assert.Single(dev.Projects);
        Assert.Equal(("Quiet", true, false), (row.Name, row.IsMember, row.Counts.HasActivity));
        Assert.Empty(dev.Timeline);
    }

    [Fact]
    public async Task Someone_with_no_activity_gets_an_empty_report()
    {
        var report = await sut.GetAsync("dev");

        Assert.Empty(report.Projects);
        Assert.False(report.Totals.HasActivity);
        Assert.Empty(report.Timeline);
    }
}
