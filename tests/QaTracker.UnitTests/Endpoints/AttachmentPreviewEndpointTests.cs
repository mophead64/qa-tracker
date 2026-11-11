using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
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
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.Storage;

namespace QaTracker.UnitTests.Endpoints;

/// <summary>The inline preview endpoint behind the attachment preview modal.</summary>
public sealed class AttachmentPreviewEndpointTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-10T10:00:00Z", CultureInfo.InvariantCulture));
    private IDbContextFactory<ApplicationDbContext> factory = null!;
    private WebApplication app = null!;
    private HttpClient client = null!;
    private Guid projectId;

    public async Task InitializeAsync()
    {
        connection.Open();
        factory = new SingleConnectionFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = "qa-1", UserName = "qa1@test.local" });
            await db.SaveChangesAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(factory);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton<IFileStorage>(new FakeFileStorage());
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<AttachmentService>();
        builder.Services.AddScoped<ProjectService>();
        builder.Services.AddScoped<NotificationService>();
        builder.Services.AddScoped<SystemSettingsService>();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddAuthorization();

        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").DisableAntiforgery().MapAttachmentEndpoints();
        await app.StartAsync();
        client = app.GetTestClient();

        await using var scope = app.Services.CreateAsyncScope();
        projectId = (await scope.ServiceProvider.GetRequiredService<ProjectService>().CreateAsync("Proj", null, null, [], "qa-1")).Id;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task<Guid> UploadAsync(string fileName, string claimedType, byte[] bytes)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var view = await scope.ServiceProvider.GetRequiredService<AttachmentService>().UploadAsync(
            AttachmentOwner.Project, projectId, new MemoryStream(bytes), fileName, claimedType, bytes.Length, null, "qa-1");
        return view.Id;
    }

    [Fact]
    public async Task An_image_is_served_inline_with_its_canonical_type_and_nosniff()
    {
        // The uploader's browser claimed something else; the extension decides what we serve.
        var id = await UploadAsync("screen.png", "text/html", [0x89, 0x50, 0x4E, 0x47]);

        var response = await client.GetAsync($"/attachments/{id}/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Video_supports_byte_ranges()
    {
        var id = await UploadAsync("clip.mp4", "video/mp4", Encoding.ASCII.GetBytes("0123456789"));
        var request = new HttpRequestMessage(HttpMethod.Get, $"/attachments/{id}/preview");
        request.Headers.Range = new RangeHeaderValue(2, 5);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("2345", await response.Content.ReadAsStringAsync());
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_pdf_has_no_restrictive_policy_so_the_browser_viewer_works()
    {
        var id = await UploadAsync("spec.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4"));

        var response = await client.GetAsync($"/attachments/{id}/preview");

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Theory]
    [InlineData("diagram.svg", "image/svg+xml")]
    [InlineData("page.html", "text/html")]
    [InlineData("notes.txt", "text/plain")]
    public async Task Anything_off_the_allow_list_is_not_previewable(string fileName, string claimedType)
    {
        var id = await UploadAsync(fileName, claimedType, Encoding.ASCII.GetBytes("<script>alert(1)</script>"));

        var response = await client.GetAsync($"/attachments/{id}/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_attachment_is_not_found() =>
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/attachments/{Guid.NewGuid()}/preview")).StatusCode);

    private sealed class SingleConnectionFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "qa-1"), new Claim(ClaimTypes.Role, Roles.QA)], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
