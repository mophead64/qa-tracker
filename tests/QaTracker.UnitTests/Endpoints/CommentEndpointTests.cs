using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.Storage;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Endpoints;

/// <summary>
/// Drives the real defect and test-case comment POST endpoints over an in-memory test server
/// (only those endpoints, signed in as a QA user, no antiforgery): the optional "mentions"
/// field, blank bodies, the redirect target, and the server-side file rejections that the
/// browser's own checks normally pre-empt.
/// </summary>
public sealed class CommentEndpointTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-21T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly SwitchableStorage storage = new();
    private IDbContextFactory<ApplicationDbContext> factory = null!;
    private WebApplication app = null!;
    private HttpClient client = null!;
    private Guid projectId;
    private Guid defectId;
    private Guid testCaseId;

    public async Task InitializeAsync()
    {
        connection.Open();
        factory = new SingleConnectionFactory(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(
                new ApplicationUser { Id = "qa-1", UserName = "qa1@test.local", Email = "qa1@test.local" },
                new ApplicationUser { Id = "dev-1", UserName = "dev1@test.local", Email = "dev1@test.local" });
            await db.SaveChangesAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(factory);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton<IFileStorage>(storage);
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<AttachmentService>();
        builder.Services.AddScoped<ProjectService>();
        builder.Services.AddScoped<TestScopeService>();
        builder.Services.AddScoped<TestCaseService>();
        builder.Services.AddScoped<NotificationService>();
        builder.Services.AddScoped<DefectService>();
        builder.Services.AddScoped<SystemSettingsService>();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, QaAuthHandler>("Test", null);
        builder.Services.AddAuthorization();

        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        // Antiforgery isn't wired in this host, so opt the mapped endpoints out of it.
        var routes = app.MapGroup("").DisableAntiforgery();
        routes.MapDefectEndpoints();
        routes.MapTestCaseEndpoints();
        await app.StartAsync();
        client = app.GetTestClient();

        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var projects = sp.GetRequiredService<ProjectService>();
        projectId = (await projects.CreateAsync("Proj", null, null, [], "qa-1")).Id;
        await projects.AddMembersAsync(projectId, ["qa-1", "dev-1"]);
        defectId = (await sp.GetRequiredService<DefectService>()
            .CreateAsync(projectId, new DefectInput("Modal never opens", null, null, null, null), "qa-1")).Id;
        var scopeId = (await sp.GetRequiredService<TestScopeService>()
            .CreateAsync(projectId, TestCaseKind.Functional, "Auth", "qa-1")).Id;
        testCaseId = (await sp.GetRequiredService<TestCaseService>()
            .CreateAsync(scopeId, new TestCaseInput("Login works", null), "qa-1")).Id;

        // The dev heard about the new defect; start each test with a clean inbox.
        await sp.GetRequiredService<NotificationService>().MarkAllReadAsync("dev-1");
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        await connection.DisposeAsync();
    }

    private string DefectComments => $"/projects/{projectId}/defects/{defectId}/comments";

    private string TestCaseComments => $"/test-cases/{testCaseId}/comments";

    private static MultipartFormDataContent Form(
        string body, string[]? mentions = null, string? returnUrl = null, int files = 0)
    {
        var form = new MultipartFormDataContent { { new StringContent(body), "body" } };
        foreach (var id in mentions ?? [])
        {
            form.Add(new StringContent(id), "mentions");
        }

        if (returnUrl is not null)
        {
            form.Add(new StringContent(returnUrl), "returnUrl");
        }

        for (var i = 0; i < files; i++)
        {
            var file = new ByteArrayContent("content"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(file, "files", $"file-{i}.txt");
        }

        return form;
    }

    private static string Location(HttpResponseMessage response) =>
        response.Headers.Location?.OriginalString ?? "";

    private async Task<int> CountAsync<T>(Func<ApplicationDbContext, IQueryable<T>> set)
    {
        await using var db = factory.CreateDbContext();
        return await set(db).CountAsync();
    }

    private Task<int> DefectCommentCount() => CountAsync(db => db.DefectComments);

    private Task<int> TestCaseCommentCount() => CountAsync(db => db.TestCaseComments);

    private Task<int> MentionsForDev() =>
        CountAsync(db => db.Notifications.Where(n => n.UserId == "dev-1" && n.Message.Contains("mentioned you")));

    // ---- defect comments ---------------------------------------------------------------

    [Fact]
    public async Task Defect_comment_without_a_mentions_field_is_stored_and_redirects_back()
    {
        var response = await client.PostAsync(DefectComments, Form("plain note"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/projects/{projectId}/defects/{defectId}", Location(response));
        Assert.Equal(1, await DefectCommentCount());
        Assert.Equal(0, await MentionsForDev());
    }

    [Fact]
    public async Task Defect_comment_mentions_field_notifies_each_mentioned_person()
    {
        var response = await client.PostAsync(DefectComments, Form("@dev1@test.local look", ["dev-1"]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, await MentionsForDev());
    }

    [Fact]
    public async Task Defect_comment_with_a_blank_body_is_ignored()
    {
        var response = await client.PostAsync(DefectComments, Form("   "));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, await DefectCommentCount());
    }

    [Fact]
    public async Task Defect_comment_with_too_many_files_is_rejected_with_a_message_and_not_stored()
    {
        var response = await client.PostAsync(DefectComments, Form("note", files: AttachmentEndpoints.MaxFilesPerComment + 1));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith($"/projects/{projectId}/defects/{defectId}?commentError=", Location(response));
        Assert.Contains("at most 10", Uri.UnescapeDataString(Location(response)));
        Assert.Equal(0, await DefectCommentCount());
    }

    [Fact]
    public async Task Defect_comment_with_files_is_rejected_when_storage_is_not_configured()
    {
        storage.IsConfigured = false;

        var response = await client.PostAsync(DefectComments, Form("note", files: 1));

        Assert.Contains("commentError=", Location(response));
        Assert.Contains("storage", Uri.UnescapeDataString(Location(response)));
        Assert.Equal(0, await DefectCommentCount());
    }

    [Fact]
    public async Task Defect_comment_without_files_still_works_when_storage_is_not_configured()
    {
        storage.IsConfigured = false;

        var response = await client.PostAsync(DefectComments, Form("note"));

        Assert.DoesNotContain("commentError", Location(response));
        Assert.Equal(1, await DefectCommentCount());
    }

    [Fact]
    public async Task Defect_comment_is_removed_again_when_storing_its_files_fails()
    {
        storage.FailOnPut = true;

        var response = await client.PostAsync(DefectComments, Form("note", files: 1));

        Assert.Contains("commentError=", Location(response));
        Assert.Contains("wasn't added", Uri.UnescapeDataString(Location(response)));
        Assert.Equal(0, await DefectCommentCount());
    }

    [Fact]
    public async Task Defect_comment_posted_as_json_is_a_client_error_not_a_server_error()
    {
        var response = await client.PostAsync(DefectComments,
            new StringContent("{\"body\":\"x\"}", System.Text.Encoding.UTF8, "application/json"));

        Assert.InRange((int)response.StatusCode, 400, 499);
        Assert.Equal(0, await DefectCommentCount());
    }

    // ---- test-case comments ------------------------------------------------------------

    [Fact]
    public async Task Test_case_comment_notifies_the_mentioned_person_and_redirects_to_the_return_url()
    {
        var response = await client.PostAsync(TestCaseComments,
            Form("@dev1@test.local fails", ["dev-1"], returnUrl: "/projects/x/back"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/projects/x/back", Location(response));
        Assert.Equal(1, await TestCaseCommentCount());
        Assert.Equal(1, await MentionsForDev());
    }

    [Fact]
    public async Task Test_case_comment_without_a_mentions_field_is_stored_without_notifications()
    {
        await client.PostAsync(TestCaseComments, Form("note", returnUrl: "/back"));

        Assert.Equal(1, await TestCaseCommentCount());
        Assert.Equal(0, await MentionsForDev());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.example/path")]
    [InlineData("https://evil.example/")]
    public async Task Test_case_comment_only_redirects_to_same_site_paths(string? returnUrl)
    {
        var response = await client.PostAsync(TestCaseComments, Form("note", returnUrl: returnUrl));

        Assert.Equal("/", Location(response));
    }

    [Fact]
    public async Task Test_case_comment_error_is_appended_to_the_return_url_query()
    {
        var response = await client.PostAsync(TestCaseComments,
            Form("note", returnUrl: "/cases/1?tab=results", files: AttachmentEndpoints.MaxFilesPerComment + 1));

        Assert.StartsWith("/cases/1?tab=results&commentError=", Location(response));
        Assert.Equal(0, await TestCaseCommentCount());
    }

    [Fact]
    public async Task Test_case_comment_is_removed_again_when_storing_its_files_fails()
    {
        storage.FailOnPut = true;

        var response = await client.PostAsync(TestCaseComments, Form("note", returnUrl: "/back", files: 1));

        Assert.StartsWith("/back?commentError=", Location(response));
        Assert.Equal(0, await TestCaseCommentCount());
    }

    // ---- host plumbing -----------------------------------------------------------------

    private sealed class SingleConnectionFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private sealed class SwitchableStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> objects = [];

        public bool IsConfigured { get; set; } = true;

        public bool FailOnPut { get; set; }

        public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        {
            if (FailOnPut)
            {
                throw new IOException("storage down");
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            objects[key] = buffer.ToArray();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream(objects[key]));

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            objects.Remove(key);
            return Task.CompletedTask;
        }
    }

    /// <summary>Signs every request in as the QA user "qa-1".</summary>
    private sealed class QaAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "qa-1"), new Claim(ClaimTypes.Role, Roles.QA)], "Test");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
