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

public sealed class SystemStatsServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-05T09:00:00Z", CultureInfo.InvariantCulture));

    public SystemStatsServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        factory = new TestDbContextFactory(options);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "qa", Email = "qa@test.local" });
        db.SaveChanges();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private SystemStatsService CreateSut() => new(factory);

    [Fact]
    public async Task GetAsync_returns_zero_counts_for_an_empty_database()
    {
        var sut = CreateSut();

        var stats = await sut.GetAsync();

        Assert.Equal(new SystemStats(0, 0, 0, 0), stats);
    }

    [Fact]
    public async Task GetAsync_counts_projects_test_cases_defects_and_files()
    {
        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var projects = new ProjectService(factory, time, attachments);
        var scopes = new TestScopeService(factory, time, projects, attachments);
        var cases = new TestCaseService(factory, time, attachments);
        var defects = new DefectService(factory, time, projects, attachments, new NotificationService(factory, time));

        var project = await projects.CreateAsync("Proj", null, [], "user-1");
        var scope = await scopes.CreateAsync(project.Id, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(scope.Id, new TestCaseInput("Log in", null), "user-1");
        await cases.CreateAsync(scope.Id, new TestCaseInput("Log out", null), "user-1");
        await defects.CreateAsync(project.Id,
            new DefectInput("Broken button", null, null, null, DefectSeverity.Low, null, []), "user-1");

        await using (var db = factory.CreateDbContext())
        {
            db.Attachments.Add(new Attachment
            {
                Id = Guid.NewGuid(),
                FileName = "spec.pdf",
                ContentType = "application/pdf",
                SizeBytes = 100,
                StorageKey = "key-1",
                UploadedUtc = time.GetUtcNow(),
                UploadedById = "user-1",
                ProjectId = project.Id,
            });
            await db.SaveChangesAsync();
        }

        var sut = CreateSut();
        var stats = await sut.GetAsync();

        Assert.Equal(new SystemStats(1, 2, 1, 1), stats);
    }

    public void Dispose() => connection.Dispose();
}
