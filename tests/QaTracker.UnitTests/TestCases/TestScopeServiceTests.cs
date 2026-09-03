using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
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

        projects = new ProjectService(factory, time);
        projectId = projects.CreateAsync("Proj", null, [], "user-1").GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private TestScopeService CreateSut() => new(factory, time, projects);
    private TestCaseService Cases() => new(factory, time);

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

    public void Dispose() => connection.Dispose();
}
