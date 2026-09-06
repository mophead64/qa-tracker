using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Attachments;

public sealed class AttachmentServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-05T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly FakeFileStorage storage = new();
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly Guid projectId;
    private readonly Guid scopeId;
    private readonly Guid testCaseId;
    private readonly Guid defectId;

    public AttachmentServiceTests()
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
            db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "qa@test.local", Email = "qa@test.local" });
            db.SaveChanges();
        }

        var attachmentsForSetup = CreateSut();
        projects = new ProjectService(factory, time, attachmentsForSetup);
        projectId = projects.CreateAsync("Proj", null, [], "user-1").GetAwaiter().GetResult().Id;
        scopes = new TestScopeService(factory, time, projects, attachmentsForSetup);
        scopeId = scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1").GetAwaiter().GetResult().Id;
        cases = new TestCaseService(factory, time, attachmentsForSetup);
        testCaseId = cases.CreateAsync(scopeId, new TestCaseInput("A scenario", null), "user-1")
            .GetAwaiter().GetResult().Id;
        defects = new DefectService(factory, time, projects, attachmentsForSetup, new NotificationService(factory, time));
        defectId = defects.CreateAsync(projectId, new DefectInput("Bug", null, null, null, DefectSeverity.Medium, null), "user-1")
            .GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private AttachmentService CreateSut() => new(factory, storage, time, NullLogger<AttachmentService>.Instance);

    private static MemoryStream Content(string text = "hello") => new(Encoding.UTF8.GetBytes(text));

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task UploadAsync_stores_the_object_and_a_row_for_each_owner_kind()
    {
        var sut = CreateSut();

        var onProject = await sut.UploadAsync(AttachmentOwner.Project, projectId, Content(), "spec.pdf", "application/pdf", 5, null, "user-1");
        var onCase = await sut.UploadAsync(AttachmentOwner.TestCase, testCaseId, Content(), "data.csv", "text/csv", 5, "sample data", "user-1");
        var onDefect = await sut.UploadAsync(AttachmentOwner.Defect, defectId, Content(), "screenshot.png", "image/png", 5, null, "user-1");

        Assert.Equal(onProject.Id, Assert.Single(await sut.ListForProjectAsync(projectId)).Id);
        Assert.Equal(onCase.Id, Assert.Single(await sut.ListForTestCaseAsync(testCaseId)).Id);
        Assert.Equal(onDefect.Id, Assert.Single(await sut.ListForDefectAsync(defectId)).Id);
        Assert.Equal(3, storage.Count);
    }

    [Fact]
    public async Task UploadAsync_trims_the_description_and_stores_the_uploader()
    {
        var sut = CreateSut();

        var uploaded = await sut.UploadAsync(AttachmentOwner.Project, projectId, Content(), "notes.txt", "text/plain", 5, "  a caption  ", "user-1");

        Assert.Equal("a caption", uploaded.Description);
        Assert.Equal("user-1", uploaded.UploadedById);
        Assert.Equal(time.GetUtcNow(), uploaded.UploadedUtc);
    }

    [Fact]
    public async Task ListForProjectAsync_orders_by_upload_order()
    {
        var sut = CreateSut();
        await sut.UploadAsync(AttachmentOwner.Project, projectId, Content(), "first.txt", "text/plain", 5, null, "user-1");
        await sut.UploadAsync(AttachmentOwner.Project, projectId, Content(), "second.txt", "text/plain", 5, null, "user-1");

        var list = await sut.ListForProjectAsync(projectId);

        Assert.Equal(["first.txt", "second.txt"], list.Select(a => a.FileName));
    }

    [Fact]
    public async Task DeleteAsync_removes_the_row_and_the_stored_object()
    {
        var sut = CreateSut();
        var uploaded = await sut.UploadAsync(AttachmentOwner.Project, projectId, Content(), "spec.pdf", "application/pdf", 5, null, "user-1");

        await sut.DeleteAsync(uploaded.Id);

        Assert.Empty(await sut.ListForProjectAsync(projectId));
        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public async Task DeleteAsync_on_an_unknown_id_is_a_no_op()
    {
        var sut = CreateSut();
        var uploaded = await sut.UploadAsync(AttachmentOwner.Project, projectId, Content(), "spec.pdf", "application/pdf", 5, null, "user-1");

        await sut.DeleteAsync(Guid.NewGuid());

        Assert.Single(await sut.ListForProjectAsync(projectId), a => a.Id == uploaded.Id);
    }

    [Fact]
    public async Task OpenAsync_returns_the_stored_bytes_and_metadata()
    {
        var sut = CreateSut();
        var uploaded = await sut.UploadAsync(AttachmentOwner.Project, projectId, Content("payload"), "spec.pdf", "application/pdf", 7, null, "user-1");

        var content = await sut.OpenAsync(uploaded.Id);

        Assert.NotNull(content);
        Assert.Equal("application/pdf", content.ContentType);
        Assert.Equal("spec.pdf", content.FileName);
        using var reader = new StreamReader(content.Content);
        Assert.Equal("payload", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenAsync_returns_null_for_an_unknown_id()
    {
        var sut = CreateSut();

        Assert.Null(await sut.OpenAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_the_project_purges_storage_and_cascades_the_rows()
    {
        var sut = CreateSut();
        await sut.UploadAsync(AttachmentOwner.Project, projectId, Content(), "spec.pdf", "application/pdf", 5, null, "user-1");
        Assert.Equal(1, storage.Count);

        await projects.DeleteAsync(projectId);

        Assert.Equal(0, storage.Count);
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task Deleting_the_test_case_purges_its_attachments_only()
    {
        var sut = CreateSut();
        await sut.UploadAsync(AttachmentOwner.TestCase, testCaseId, Content(), "data.csv", "text/csv", 5, null, "user-1");
        var onProject = await sut.UploadAsync(AttachmentOwner.Project, projectId, Content(), "spec.pdf", "application/pdf", 5, null, "user-1");
        Assert.Equal(2, storage.Count);

        await cases.DeleteAsync(testCaseId);

        Assert.Equal(1, storage.Count);
        Assert.Single(await sut.ListForProjectAsync(projectId), a => a.Id == onProject.Id);
    }

    [Fact]
    public async Task Deleting_the_scope_purges_attachments_on_every_case_under_it()
    {
        var sut = CreateSut();
        var testCaseId2 = (await cases.CreateAsync(scopeId, new TestCaseInput("Another", null), "user-1")).Id;
        await sut.UploadAsync(AttachmentOwner.TestCase, testCaseId, Content(), "a.txt", "text/plain", 5, null, "user-1");
        await sut.UploadAsync(AttachmentOwner.TestCase, testCaseId2, Content(), "b.txt", "text/plain", 5, null, "user-1");
        Assert.Equal(2, storage.Count);

        await scopes.DeleteAsync(scopeId);

        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public async Task Deleting_the_defect_purges_its_attachments()
    {
        var sut = CreateSut();
        await sut.UploadAsync(AttachmentOwner.Defect, defectId, Content(), "screenshot.png", "image/png", 5, null, "user-1");
        Assert.Equal(1, storage.Count);

        await defects.DeleteAsync(defectId);

        Assert.Equal(0, storage.Count);
    }
}
