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

public sealed class TestScopeServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-03T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly AttachmentService attachments;
    private readonly ProjectService projects;
    private readonly Guid projectId;

    public TestScopeServiceTests()
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
            db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "qa", Email = "qa@test.local" });
            db.SaveChanges();
        }

        attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        projects = new ProjectService(factory, time, attachments);
        projectId = projects.CreateAsync("Proj", null, null, [], "user-1").GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private TestScopeService CreateSut() => new(factory, time, projects, attachments);
    private TestCaseService Cases() => new(factory, time, attachments, new QaTracker.Web.Notifications.NotificationService(factory, time));

    [Fact]
    public async Task CreateAsync_sets_defaults_and_trims()
    {
        var sut = CreateSut();

        var scope = await sut.CreateAsync(projectId, TestCaseKind.Functional, "  Authentication  ", "user-1");

        Assert.Equal("Authentication", scope.Name);
        Assert.Equal(TestCaseKind.Functional, scope.Kind);
        Assert.Equal("user-1", scope.CreatedById);
        Assert.Equal(time.GetUtcNow(), scope.CreatedUtc);
    }

    [Fact]
    public async Task CreateAsync_promotes_project_to_in_flight()
    {
        var sut = CreateSut();
        Assert.Equal(ProjectStatus.NotStarted, (await projects.GetAsync(projectId))!.Status);

        await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");

        Assert.Equal(ProjectStatus.InFlight, (await projects.GetAsync(projectId))!.Status);
    }

    [Fact]
    public async Task ListForProjectAsync_orders_functional_first_then_by_name()
    {
        var sut = CreateSut();
        await sut.CreateAsync(projectId, TestCaseKind.NonFunctional, "Performance", "user-1");
        await sut.CreateAsync(projectId, TestCaseKind.Functional, "Checkout", "user-1");
        await sut.CreateAsync(projectId, TestCaseKind.Functional, "Authentication", "user-1");

        var list = await sut.ListForProjectAsync(projectId);

        Assert.Equal(["Authentication", "Checkout", "Performance"], list.Select(s => s.Name));
    }

    [Fact]
    public async Task UpdateAsync_changes_kind_and_name()
    {
        var sut = CreateSut();
        var scope = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");

        await sut.UpdateAsync(scope.Id, TestCaseKind.NonFunctional, "Security");

        var reloaded = await sut.GetAsync(scope.Id);
        Assert.Equal(TestCaseKind.NonFunctional, reloaded!.Kind);
        Assert.Equal("Security", reloaded.Name);
    }

    [Fact]
    public async Task DeleteAsync_removes_scope_and_its_cases()
    {
        var sut = CreateSut();
        var scope = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await Cases().CreateAsync(scope.Id, new("valid creds", null), "user-1");

        await sut.DeleteAsync(scope.Id);

        Assert.Null(await sut.GetAsync(scope.Id));
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.TestCases);
    }

    [Fact]
    public async Task SummariseProjectAsync_counts_scopes_and_cases_by_result()
    {
        var sut = CreateSut();
        var cases = Cases();
        var auth = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var perf = await sut.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "user-1");
        var a = await cases.CreateAsync(auth.Id, new("scenario a", null), "user-1");
        var b = await cases.CreateAsync(auth.Id, new("scenario b", null), "user-1");
        await cases.CreateAsync(perf.Id, new("scenario c", null), "user-1");
        await cases.SetResultAsync(a.Id, TestResult.Passed);
        await cases.SetResultAsync(b.Id, TestResult.Failed);

        var summary = await sut.SummariseProjectAsync(projectId);

        Assert.Equal(new TestPlanSummary(Scopes: 2, Cases: 3, Passed: 1, Failed: 1, NotRun: 1), summary);
    }

    // Cases are ordered by CreatedUtc, so advance the clock between creations.
    private async Task<TestCase> AddCase(Guid scopeId, string scenario, TestResult result = TestResult.NotRun)
    {
        time.Advance(TimeSpan.FromMinutes(1));
        var cases = Cases();
        var created = await cases.CreateAsync(scopeId, new(scenario, null), "user-1");
        if (result != TestResult.NotRun)
        {
            await cases.SetResultAsync(created.Id, result);
        }

        return created;
    }

    [Fact]
    public async Task GetRunNavigationAsync_skips_passed_cases_within_scope()
    {
        var sut = CreateSut();
        var scope = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var a = await AddCase(scope.Id, "a", TestResult.Failed);
        await AddCase(scope.Id, "b", TestResult.Passed);
        var c = await AddCase(scope.Id, "c");
        await AddCase(scope.Id, "d", TestResult.Passed);
        var e = await AddCase(scope.Id, "e");

        var nav = await sut.GetRunNavigationAsync(projectId, c.Id);

        Assert.Equal(a.Id, nav.Previous?.Id);
        Assert.Equal(e.Id, nav.Next?.Id);
        Assert.Equal(3, nav.ToAction);
    }

    [Fact]
    public async Task GetRunNavigationAsync_disables_previous_when_first_or_all_earlier_passed()
    {
        var sut = CreateSut();
        var earlier = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Accounts", "user-1");
        await AddCase(earlier.Id, "earlier passed", TestResult.Passed);
        var scope = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var first = await AddCase(scope.Id, "a");
        await AddCase(scope.Id, "b", TestResult.Passed);
        var third = await AddCase(scope.Id, "c");

        Assert.Null((await sut.GetRunNavigationAsync(projectId, first.Id)).Previous);
        Assert.Equal(first.Id, (await sut.GetRunNavigationAsync(projectId, third.Id)).Previous?.Id);

        await Cases().SetResultAsync(first.Id, TestResult.Passed);
        Assert.Null((await sut.GetRunNavigationAsync(projectId, third.Id)).Previous);
    }

    [Fact]
    public async Task GetRunNavigationAsync_next_moves_to_first_unpassed_case_of_a_later_scope()
    {
        var sut = CreateSut();
        // Listed functional first, then by name: Auth, Billing, Perf.
        var perf = await sut.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "user-1");
        var billing = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Billing", "user-1");
        var auth = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var last = await AddCase(auth.Id, "auth last");
        await AddCase(auth.Id, "auth passed", TestResult.Passed);
        await AddCase(billing.Id, "billing passed", TestResult.Passed);
        var target = await AddCase(perf.Id, "perf failed", TestResult.Failed);

        var nav = await sut.GetRunNavigationAsync(projectId, last.Id);

        Assert.Equal(target.Id, nav.Next?.Id);
        Assert.Equal(perf.Id, nav.Next?.TestScopeId);
        Assert.Null(nav.Previous);
    }

    [Fact]
    public async Task GetRunNavigationAsync_previous_moves_back_to_last_unpassed_case_of_an_earlier_scope()
    {
        var sut = CreateSut();
        // Listed functional first, then by name: Auth, Billing, Perf.
        var perf = await sut.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "user-1");
        var billing = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Billing", "user-1");
        var auth = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await AddCase(auth.Id, "auth failed early", TestResult.Failed);
        var target = await AddCase(auth.Id, "auth failed late", TestResult.Failed);
        await AddCase(auth.Id, "auth passed", TestResult.Passed);
        await AddCase(billing.Id, "billing passed", TestResult.Passed);
        await AddCase(perf.Id, "perf passed", TestResult.Passed);
        var current = await AddCase(perf.Id, "perf current");

        var nav = await sut.GetRunNavigationAsync(projectId, current.Id);

        Assert.Equal(target.Id, nav.Previous?.Id);
        Assert.Equal(auth.Id, nav.Previous?.TestScopeId);
    }

    [Fact]
    public async Task GetRunNavigationAsync_disables_next_when_nothing_left_to_run()
    {
        var sut = CreateSut();
        var auth = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var perf = await sut.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "user-1");
        var current = await AddCase(auth.Id, "a");
        await AddCase(auth.Id, "b", TestResult.Passed);
        await AddCase(perf.Id, "c", TestResult.Passed);

        Assert.Null((await sut.GetRunNavigationAsync(projectId, current.Id)).Next);
    }

    [Fact]
    public async Task GetRunNavigationAsync_steps_through_every_case_once_all_passed()
    {
        var sut = CreateSut();
        var auth = await sut.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var perf = await sut.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "user-1");
        var a = await AddCase(auth.Id, "a", TestResult.Passed);
        var b = await AddCase(auth.Id, "b", TestResult.Passed);
        var c = await AddCase(perf.Id, "c", TestResult.Passed);

        var first = await sut.GetRunNavigationAsync(projectId, a.Id);
        var middle = await sut.GetRunNavigationAsync(projectId, b.Id);
        var last = await sut.GetRunNavigationAsync(projectId, c.Id);

        Assert.Equal(0, middle.ToAction);
        Assert.Null(first.Previous);
        Assert.Equal(b.Id, first.Next?.Id);
        Assert.Equal(a.Id, middle.Previous?.Id);
        Assert.Equal(c.Id, middle.Next?.Id);
        Assert.Equal(b.Id, last.Previous?.Id);
        Assert.Null(last.Next);
    }

    public void Dispose() => connection.Dispose();
}
