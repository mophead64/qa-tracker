using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Dashboard;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Dashboard;

public sealed class ProjectActionsServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-04T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly DefectService defects;
    private readonly TestCaseService cases;
    private readonly Guid projectId;
    private readonly Guid scopeId;

    public ProjectActionsServiceTests()
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
            db.Users.AddRange(
                new ApplicationUser { Id = "user-1", UserName = "qa@test.local", Email = "qa@test.local", FullName = "QA One" },
                new ApplicationUser { Id = "user-2", UserName = "dev@test.local", Email = "dev@test.local", FullName = "Dev One" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var projects = new ProjectService(factory, time, attachments);
        projectId = projects.CreateAsync("Proj", null, null, [], "user-1").GetAwaiter().GetResult().Id;
        var scopes = new TestScopeService(factory, time, projects, attachments);
        scopeId = scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1").GetAwaiter().GetResult().Id;
        cases = new TestCaseService(factory, time, attachments);
        defects = new DefectService(factory, time, projects, attachments, new NotificationService(factory, time));
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private ProjectActionsService CreateSut() => new(factory);

    private async Task<TestCase> CreateCaseAsync(TestResult result, string scenario = "A scenario")
    {
        var tc = await cases.CreateAsync(scopeId, new TestCaseInput(scenario, null), "user-1");
        if (result != TestResult.NotRun)
        {
            await cases.SetResultAsync(tc.Id, result);
        }
        return tc;
    }

    private async Task<Defect> CreateDefectAsync(DefectStatus status, string? assigneeId = null, Guid? linkedCaseId = null)
    {
        var defect = await defects.CreateAsync(
            projectId,
            new DefectInput("A defect", null, null, null, assigneeId),
            "user-1");
        await defects.SetStatusAsync(defect.Id, status);
        if (linkedCaseId is { } caseId)
        {
            await defects.LinkTestCaseAsync(defect.Id, caseId);
        }
        return defect;
    }

    [Fact]
    public async Task DefectsToVerify_only_includes_ToCheck_defects_assigned_to_the_user()
    {
        var mine = await CreateDefectAsync(DefectStatus.ToCheck, "user-1");
        await CreateDefectAsync(DefectStatus.ToCheck, "user-2"); // someone else's
        await CreateDefectAsync(DefectStatus.NotFixed, "user-1"); // wrong status

        var sut = CreateSut();
        var actions = await sut.GetAsync(projectId, "user-1");

        Assert.Equal(mine.Id, Assert.Single(actions.DefectsToVerify).Id);
    }

    [Fact]
    public async Task DefectsToFix_includes_NotFixed_and_Fixing_assigned_to_the_user()
    {
        var notFixed = await CreateDefectAsync(DefectStatus.NotFixed, "user-1");
        var fixing = await CreateDefectAsync(DefectStatus.Fixing, "user-1");
        await CreateDefectAsync(DefectStatus.ToCheck, "user-1"); // not a "to fix" status
        await CreateDefectAsync(DefectStatus.NotFixed, "user-2"); // someone else's

        var sut = CreateSut();
        var actions = await sut.GetAsync(projectId, "user-1");

        Assert.Equal(
            new[] { notFixed.Id, fixing.Id }.ToHashSet(),
            actions.DefectsToFix.Select(d => d.Id).ToHashSet());
    }

    [Fact]
    public async Task Passed_test_with_an_open_defect_is_flagged()
    {
        var tc = await CreateCaseAsync(TestResult.Passed);
        var openDefect = await CreateDefectAsync(DefectStatus.NotFixed, linkedCaseId: tc.Id);

        var sut = CreateSut();
        var actions = await sut.GetAsync(projectId, "user-1");

        var followUp = Assert.Single(actions.TestCaseFollowUps);
        Assert.Equal(tc.Id, followUp.Case.Id);
        Assert.Equal(TestCaseFollowUpReason.PassedWithOpenDefect, followUp.Reason);
        Assert.Equal(openDefect.Id, Assert.Single(followUp.RelatedDefects).Id);
    }

    [Fact]
    public async Task Passed_test_with_only_closed_defects_is_not_flagged()
    {
        var tc = await CreateCaseAsync(TestResult.Passed);
        await CreateDefectAsync(DefectStatus.Fixed, linkedCaseId: tc.Id);

        var sut = CreateSut();
        var actions = await sut.GetAsync(projectId, "user-1");

        Assert.Empty(actions.TestCaseFollowUps);
    }

    [Fact]
    public async Task Failed_test_with_no_defects_is_flagged()
    {
        var tc = await CreateCaseAsync(TestResult.Failed);

        var sut = CreateSut();
        var actions = await sut.GetAsync(projectId, "user-1");

        var followUp = Assert.Single(actions.TestCaseFollowUps);
        Assert.Equal(tc.Id, followUp.Case.Id);
        Assert.Equal(TestCaseFollowUpReason.FailedNoDefect, followUp.Reason);
        Assert.Empty(followUp.RelatedDefects);
    }

    [Fact]
    public async Task Failed_test_whose_defects_are_all_closed_is_ready_to_retest()
    {
        var tc = await CreateCaseAsync(TestResult.Failed);
        var fixedDefect = await CreateDefectAsync(DefectStatus.Fixed, linkedCaseId: tc.Id);
        var notADefect = await CreateDefectAsync(DefectStatus.NotADefect, linkedCaseId: tc.Id);

        var sut = CreateSut();
        var actions = await sut.GetAsync(projectId, "user-1");

        var followUp = Assert.Single(actions.TestCaseFollowUps);
        Assert.Equal(TestCaseFollowUpReason.FailedReadyToRetest, followUp.Reason);
        Assert.Equal(
            new[] { fixedDefect.Id, notADefect.Id }.ToHashSet(),
            followUp.RelatedDefects.Select(d => d.Id).ToHashSet());
    }

    [Fact]
    public async Task Failed_test_with_at_least_one_open_defect_is_not_flagged()
    {
        var tc = await CreateCaseAsync(TestResult.Failed);
        await CreateDefectAsync(DefectStatus.Fixed, linkedCaseId: tc.Id);
        await CreateDefectAsync(DefectStatus.Fixing, linkedCaseId: tc.Id); // still open

        var sut = CreateSut();
        var actions = await sut.GetAsync(projectId, "user-1");

        Assert.Empty(actions.TestCaseFollowUps);
    }

    [Fact]
    public async Task NotRun_test_cases_never_produce_a_follow_up()
    {
        var tc = await CreateCaseAsync(TestResult.NotRun);
        await CreateDefectAsync(DefectStatus.NotFixed, linkedCaseId: tc.Id);

        var sut = CreateSut();
        var actions = await sut.GetAsync(projectId, "user-1");

        Assert.Empty(actions.TestCaseFollowUps);
    }

    public void Dispose() => connection.Dispose();
}
