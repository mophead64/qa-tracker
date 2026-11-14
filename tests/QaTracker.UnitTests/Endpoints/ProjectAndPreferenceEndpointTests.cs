using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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
using QaTracker.Web.Auth;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Preferences;
using QaTracker.Web.Projects;
using QaTracker.Web.Storage;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Endpoints;

/// <summary>
/// The project endpoints the dashboard uses (live stamp, "#" picker list, status with the
/// completion warning) and the per-user preference posts, over an in-memory test server with the
/// real authorization policies. The signed-in user is picked per request with an X-Test-User
/// header: "qa" (QA), "dev" (Dev) or "viewer" (Viewer).
/// </summary>
public sealed class ProjectAndPreferenceEndpointTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-14T10:00:00Z", CultureInfo.InvariantCulture));
    private WebApplication app = null!;
    private HttpClient client = null!;
    private Guid projectId;
    private Guid caseId;

    public async Task InitializeAsync()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            foreach (var (id, role) in new[] { ("qa", Roles.QA), ("dev", Roles.Dev), ("viewer", Roles.Viewer) })
            {
                db.Users.Add(new ApplicationUser { Id = id, UserName = $"{id}@test.local", NormalizedUserName = $"{id}@TEST.LOCAL", Email = $"{id}@test.local" });
            }
            await db.SaveChangesAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContextFactory<ApplicationDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton<IFileStorage>(new FakeFileStorage());
        builder.Services.AddMemoryCache();
        foreach (var service in new[] { typeof(AttachmentService), typeof(ProjectService), typeof(TestScopeService), typeof(TestCaseService),
                     typeof(NotificationService), typeof(DefectService), typeof(SystemSettingsService), typeof(CommentReferenceService) })
        {
            builder.Services.AddScoped(service);
        }
        builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.ManageProjects, p => p.RequireAuthenticatedUser().AddRequirements(new ManageAreaRequirement(ManageableArea.Projects)));
        builder.Services.AddScoped<IAuthorizationHandler, ManageAreaHandler>();

        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var routes = app.MapGroup("").DisableAntiforgery();
        routes.MapProjectEndpoints();
        routes.MapPreferenceEndpoints();
        routes.MapCommentReferenceEndpoints();
        await app.StartAsync();
        client = app.GetTestClient();

        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        projectId = (await sp.GetRequiredService<ProjectService>().CreateAsync("Proj", null, null, [], "qa")).Id;
        var scopeId = (await sp.GetRequiredService<TestScopeService>().CreateAsync(projectId, TestCaseKind.Functional, "Auth", "qa")).Id;
        caseId = (await sp.GetRequiredService<TestCaseService>().CreateAsync(scopeId, new TestCaseInput("Signs in", null), "qa")).Id;
        await sp.GetRequiredService<DefectService>().CreateAsync(projectId, new DefectInput("Login broken", null, null, null, null), "qa");
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task<HttpResponseMessage> PostAsync(string user, string url, params (string Key, string Value)[] form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))),
        };
        request.Headers.Add("X-Test-User", user);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Test-User", "viewer");
        return await client.SendAsync(request);
    }

    private async Task<ApplicationUser> UserAsync(string id)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    // ---- preferences --------------------------------------------------------------------------

    [Fact]
    public async Task A_preference_is_saved_and_the_user_goes_back_where_they_were()
    {
        var back = $"/projects/{projectId}/defects";
        var response = await PostAsync("dev", "/preferences/defect-filter", ("filter", "1"), ("returnUrl", back));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(back, response.Headers.Location?.OriginalString);
        Assert.Equal(DefectListFilter.Open, (await UserAsync("dev")).DefectListFilter);
    }

    [Theory]
    [InlineData("//evil.example/phish")]
    [InlineData("/\\evil.example")]
    [InlineData("https://evil.example")]
    public async Task Off_site_return_urls_go_home_instead(string returnUrl)
    {
        var response = await PostAsync("dev", "/preferences/test-case-layout", ("layout", "Collapsed"), ("returnUrl", returnUrl));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Equal(TestScopeLayout.Collapsed, (await UserAsync("dev")).TestScopeLayout);
    }

    [Fact]
    public async Task An_unknown_preference_value_is_ignored()
    {
        await PostAsync("qa", "/preferences/test-case-filter", ("filter", "999"), ("returnUrl", "/"));

        Assert.Equal(TestCaseListFilter.All, (await UserAsync("qa")).TestCaseListFilter);
    }

    // ---- project dashboard endpoints ----------------------------------------------------------

    [Fact]
    public async Task The_dashboard_stamp_is_json_uncached_and_404_for_an_unknown_project()
    {
        var response = await GetAsync($"/projects/{projectId}/live");

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Matches("^[0-9a-f]{16}$", body!["version"]);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/projects/{Guid.NewGuid()}/live")).StatusCode);
    }

    [Fact]
    public async Task The_hash_picker_lists_the_projects_defects_then_test_cases()
    {
        var options = await (await GetAsync($"/projects/{projectId}/comment-references"))
            .Content.ReadFromJsonAsync<List<Dictionary<string, string>>>();

        Assert.NotNull(options);
        Assert.Equal(["D-1", "T-1"], options.Select(o => o["ref"]));
        Assert.Equal("Login broken", options[0]["title"]);
        Assert.Equal("test case", options[1]["kind"]);
    }

    [Fact]
    public async Task A_viewer_cannot_change_a_projects_status()
    {
        var response = await PostAsync("viewer", $"/projects/{projectId}/status", ("status", nameof(ProjectStatus.InFlight)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Completing_with_work_outstanding_asks_first_and_completes_once_confirmed()
    {
        var url = $"/projects/{projectId}/status";

        var first = await PostAsync("qa", url, ("status", nameof(ProjectStatus.Complete)));
        Assert.Equal($"/projects/{projectId}?confirmComplete=true", first.Headers.Location?.OriginalString);
        Assert.NotEqual(ProjectStatus.Complete, await StatusAsync());

        var confirmed = await PostAsync("qa", url, ("status", nameof(ProjectStatus.Complete)), ("confirmed", "true"));
        Assert.Equal($"/projects/{projectId}", confirmed.Headers.Location?.OriginalString);
        Assert.Equal(ProjectStatus.Complete, await StatusAsync());
    }

    private async Task<ProjectStatus> StatusAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ProjectService>().GetAsync(projectId))!.Status;
    }

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            var role = user switch { "qa" => Roles.QA, "dev" => Roles.Dev, "viewer" => Roles.Viewer, _ => null };
            if (role is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Role, role)], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
