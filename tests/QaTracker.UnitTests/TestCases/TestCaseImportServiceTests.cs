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

namespace QaTracker.UnitTests.TestCases;

public sealed class TestCaseImportServiceTests : IDisposable
{
    private const string Header = "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink";

    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-03T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly Guid projectId;

    public TestCaseImportServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        factory = new TestDbContextFactory(options);

        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "qa", Email = "qa@test.local", FullName = "QA One" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        projects = new ProjectService(factory, time, attachments);
        var notifications = new NotificationService(factory, time);
        scopes = new TestScopeService(factory, time, projects, attachments);
        cases = new TestCaseService(factory, time, attachments);
        defects = new DefectService(factory, time, projects, attachments, notifications);
        projectId = projects.CreateAsync("Proj", null, [], "user-1").GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private TestCaseImportService Sut() => new(scopes, cases, defects);

    private static ParsedImport Parse(params string[] lines) =>
        TestCaseImportParser.Parse(Header + "\n" + string.Join("\n", lines) + "\n");

    private Task<Defect> RaiseDefectAsync(string summary = "A bug") =>
        defects.CreateAsync(projectId, new DefectInput(summary, null, null, null, null), "user-1");

    // --- BuildPlanAsync -------------------------------------------------------

    [Fact]
    public async Task Plan_reports_a_new_scope_as_an_info()
    {
        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,"));

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Infos, i => i.Message.Contains("will be created"));
        Assert.Equal(1, plan.ScopesToCreate);
        Assert.Equal(1, plan.CasesToCreate);
    }

    [Fact]
    public async Task Plan_reports_an_existing_scope_of_the_same_type_as_an_info()
    {
        await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,"));

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Infos, i => i.Message.Contains("already exists"));
    }

    [Fact]
    public async Task Plan_blocks_when_an_existing_scope_has_a_different_type()
    {
        await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Non-Functional,Login is fast,do,"));

        Assert.False(plan.CanImport);
        Assert.Contains(plan.Errors, e => e.Message.Contains("already exists as Functional"));
    }

    [Fact]
    public async Task Plan_warns_that_an_existing_case_will_be_overwritten()
    {
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", "old step"), "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,new step,"));

        var casePlan = plan.Scopes.Single().Cases.Single();
        Assert.Equal(ImportAction.Update, casePlan.Action);
        Assert.Contains(plan.Warnings, w => w.Message.Contains("steps will be overwritten"));
    }

    [Fact]
    public async Task Plan_shows_a_clean_defect_link_as_an_example()
    {
        await RaiseDefectAsync();

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,D-1"));

        Assert.Contains(plan.Infos, i => i.Message.Contains("D-1 → will be linked"));
        Assert.Single(plan.Scopes.Single().Cases.Single().DefectLinks);
    }

    [Fact]
    public async Task Plan_notes_a_defect_link_that_already_exists()
    {
        var defect = await RaiseDefectAsync();
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var tc = await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", null), "user-1");
        await defects.LinkTestCaseAsync(defect.Id, tc.Id);

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,D-1"));

        Assert.Contains(plan.Infos, i => i.Message.Contains("already linked"));
        Assert.True(plan.Scopes.Single().Cases.Single().DefectLinks.Single().AlreadyLinked);
    }

    [Fact]
    public async Task Plan_warns_about_an_unknown_defect_number()
    {
        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,D-99"));

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Warnings, w => w.Message.Contains("D-99") && w.Message.Contains("doesn't exist"));
    }

    [Fact]
    public async Task Plan_warns_about_an_unparseable_defect_token()
    {
        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,see-jira"));

        Assert.Contains(plan.Warnings, w => w.Message.Contains("isn't a valid reference"));
    }

    [Fact]
    public async Task Plan_warns_when_a_defect_is_currently_linked_elsewhere()
    {
        var defect = await RaiseDefectAsync();
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var other = await cases.CreateAsync(scope.Id, new TestCaseInput("Some other case", null), "user-1");
        await defects.LinkTestCaseAsync(defect.Id, other.Id);

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,D-1"));

        Assert.Contains(plan.Warnings, w => w.Message.Contains("currently linked to \"Some other case\""));
    }

    // --- ApplyAsync ---------------------------------------------------------

    [Fact]
    public async Task Apply_creates_scopes_cases_steps_and_links_and_activates_the_project()
    {
        await RaiseDefectAsync();
        var csv = Parse(
            "Auth,Functional,Login works,open,D-1",
            ",,,submit,",
            "Perf,Non Functional,Search is fast,,");

        var result = await Sut().ApplyAsync(projectId, csv, "user-1");

        Assert.True(result.Committed);
        Assert.Equal(2, result.ScopesCreated);
        Assert.Equal(2, result.CasesCreated);
        Assert.Equal(1, result.DefectsLinked);

        var stored = await scopes.ListForProjectAsync(projectId);
        var login = stored.Single(s => s.Name == "Auth").Cases.Single();
        Assert.Equal("open\nsubmit", login.Steps);
        Assert.Equal(ProjectStatus.InFlight, (await projects.GetAsync(projectId))!.Status);
        Assert.Single(await defects.ListForTestCaseAsync(login.Id));
    }

    [Fact]
    public async Task Apply_overwrites_steps_on_an_existing_case_but_keeps_its_result()
    {
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var tc = await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", "old"), "user-1");
        await cases.SetResultAsync(tc.Id, TestResult.Failed);

        var result = await Sut().ApplyAsync(projectId, Parse("Auth,Functional,Login works,brand new steps,"), "user-1");

        Assert.Equal(1, result.CasesUpdated);
        var reloaded = await cases.GetAsync(tc.Id);
        Assert.Equal("brand new steps", reloaded!.Steps);
        Assert.Equal(TestResult.Failed, reloaded.Result);
    }

    [Fact]
    public async Task Apply_is_idempotent()
    {
        await RaiseDefectAsync();
        var csv = Parse("Auth,Functional,Login works,open,D-1");
        var sut = Sut();

        await sut.ApplyAsync(projectId, csv, "user-1");
        var second = await sut.ApplyAsync(projectId, csv, "user-1");

        Assert.Equal(0, second.ScopesCreated);
        Assert.Equal(0, second.CasesCreated);
        Assert.Equal(1, second.CasesUpdated);
        Assert.Equal(0, second.DefectsLinked);

        Assert.Single((await scopes.ListForProjectAsync(projectId)).Single().Cases);
        var tc = (await scopes.ListForProjectAsync(projectId)).Single().Cases.Single();
        Assert.Single(await defects.ListForTestCaseAsync(tc.Id));
    }

    [Fact]
    public async Task Apply_refuses_when_the_plan_has_blocking_errors()
    {
        await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");

        var result = await Sut().ApplyAsync(projectId, Parse("Auth,Non-Functional,Login is fast,do,"), "user-1");

        Assert.False(result.Committed);
        Assert.Empty((await scopes.ListForProjectAsync(projectId)).Single().Cases);
    }

    public void Dispose() => connection.Dispose();
}
