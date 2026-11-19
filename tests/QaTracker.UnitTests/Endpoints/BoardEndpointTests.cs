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
using QaTracker.Web.Board;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.Storage;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Endpoints;

/// <summary>
/// The board's posts and polls over an in-memory test server with the real policies: who may move
/// cards, tick tasks and link items, that every item route is scoped to its own project's board,
/// and the JSON answer board.js gets for a drag. X-Test-User picks "qa", "dev" or "viewer".
/// </summary>
public sealed class BoardEndpointTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-19T10:00:00Z", CultureInfo.InvariantCulture));
    private WebApplication app = null!;
    private HttpClient client = null!;
    private Guid projectId;
    private Guid otherProjectId;
    private Guid storyId;
    private Guid otherStoryId;
    private Guid defectId;

    public async Task InitializeAsync()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            foreach (var id in new[] { "qa", "dev", "viewer" })
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
                     typeof(NotificationService), typeof(DefectService), typeof(SystemSettingsService), typeof(LocalBoardProvider),
                     typeof(BoardProviders), typeof(BoardService) })
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
        app.MapGroup("").DisableAntiforgery().MapBoardEndpoints();
        await app.StartAsync();
        client = app.GetTestClient();

        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var projects = sp.GetRequiredService<ProjectService>();
        var board = sp.GetRequiredService<BoardService>();
        projectId = (await projects.CreateAsync("Proj", null, null, [], "qa")).Id;
        otherProjectId = (await projects.CreateAsync("Other", null, null, [], "qa")).Id;
        await projects.AddMembersAsync(projectId, ["qa", "dev"]);
        await board.SetModeAsync(projectId, BoardMode.Local);
        await board.SetModeAsync(otherProjectId, BoardMode.Local);
        storyId = (await board.CreateAsync(projectId, BoardItemType.Story, new BoardItemInput("Saved cards", null, null), "qa")).Id;
        otherStoryId = (await board.CreateAsync(otherProjectId, BoardItemType.Story, new BoardItemInput("Elsewhere", null, null), "qa")).Id;
        defectId = (await sp.GetRequiredService<DefectService>().CreateAsync(projectId, new DefectInput("Card declined", null, null, null, null), "qa")).Id;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task<HttpResponseMessage> PostAsync(string user, string url, bool fetch, params (string Key, string Value)[] form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))),
        };
        request.Headers.Add("X-Test-User", user);
        if (fetch)
        {
            request.Headers.Add("X-Requested-With", "fetch");
        }
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string user = "viewer")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Test-User", user);
        return await client.SendAsync(request);
    }

    private async Task<BoardItem> ItemAsync(Guid project, Guid id)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<BoardService>().GetAsync(project, id))!;
    }

    [Fact]
    public async Task A_drag_answers_json_and_a_form_post_redirects_back()
    {
        var dragged = await PostAsync("dev", $"/projects/{projectId}/board/{storyId}/move", fetch: true,
            ("column", "InProgress"), ("index", "0"));
        Assert.Equal(HttpStatusCode.OK, dragged.StatusCode);
        Assert.True((await dragged.Content.ReadFromJsonAsync<Dictionary<string, bool>>())!["ok"]);
        Assert.Equal(BoardColumn.InProgress, (await ItemAsync(projectId, storyId)).Column);

        var picked = await PostAsync("qa", $"/projects/{projectId}/board/{storyId}/move", fetch: false, ("column", "Testing"));
        Assert.Equal(HttpStatusCode.Redirect, picked.StatusCode);
        Assert.Equal($"/projects/{projectId}/board/{storyId}", picked.Headers.Location?.OriginalString);
        Assert.Equal(BoardColumn.Testing, (await ItemAsync(projectId, storyId)).Column);
    }

    [Fact]
    public async Task Viewers_can_read_the_board_but_not_change_it()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/projects/{projectId}/board/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/projects/{projectId}/board/{storyId}/live")).StatusCode);

        var moved = await PostAsync("viewer", $"/projects/{projectId}/board/{storyId}/move", fetch: true, ("column", "Closed"));
        var task = await PostAsync("viewer", $"/projects/{projectId}/board/{storyId}/tasks", fetch: false, ("title", "Sneaky"));

        Assert.Equal(HttpStatusCode.Forbidden, moved.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, task.StatusCode);
        var story = await ItemAsync(projectId, storyId);
        Assert.Equal(BoardColumn.New, story.Column);
        Assert.Empty(story.Tasks);
    }

    [Fact]
    public async Task Item_routes_only_reach_items_on_this_projects_board()
    {
        // Another project's story, addressed through this project.
        var crossed = await PostAsync("qa", $"/projects/{projectId}/board/{otherStoryId}/move", fetch: true, ("column", "Closed"));
        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
        Assert.Equal(BoardColumn.New, (await ItemAsync(otherProjectId, otherStoryId)).Column);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/projects/{projectId}/board/{otherStoryId}/live")).StatusCode);

        // A board switched off is gone for every route.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BoardService>().SetModeAsync(otherProjectId, BoardMode.Off);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/projects/{otherProjectId}/board/live")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/projects/{otherProjectId}/board/{otherStoryId}/live")).StatusCode);
    }

    [Fact]
    public async Task Tasks_are_added_ticked_and_reordered()
    {
        await PostAsync("dev", $"/projects/{projectId}/board/{storyId}/tasks", fetch: false, ("title", "Add the table"), ("assigneeId", "dev"));
        await PostAsync("dev", $"/projects/{projectId}/board/{storyId}/tasks", fetch: false, ("title", "Write the API"));
        await PostAsync("dev", $"/projects/{projectId}/board/{storyId}/tasks", fetch: false, ("title", "   "));
        var tasks = (await ItemAsync(projectId, storyId)).Tasks.ToList();
        Assert.Equal(["Add the table", "Write the API"], tasks.Select(t => t.Title));
        Assert.Equal("dev", tasks[0].AssignedToId);

        var ticked = await PostAsync("dev", $"/projects/{projectId}/board/{tasks[1].Id}/done", fetch: false,
            ("done", "true"), ("returnUrl", $"/projects/{projectId}/board/{storyId}#tasks"));
        Assert.Equal($"/projects/{projectId}/board/{storyId}#tasks", ticked.Headers.Location?.OriginalString);

        var reordered = await PostAsync("dev", $"/projects/{projectId}/board/{tasks[1].Id}/task-order", fetch: true, ("index", "0"));
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);

        var after = (await ItemAsync(projectId, storyId)).Tasks.ToList();
        Assert.Equal(["Write the API", "Add the table"], after.Select(t => t.Title));
        Assert.True(after[0].Done);

        // A task can't have tasks of its own.
        await PostAsync("dev", $"/projects/{projectId}/board/{after[0].Id}/tasks", fetch: false, ("title", "Nested"));
        Assert.Empty((await ItemAsync(projectId, after[0].Id)).Tasks);
    }

    [Fact]
    public async Task Links_take_ticked_values_and_ignore_anything_else()
    {
        var linked = await PostAsync("qa", $"/projects/{projectId}/board/{storyId}/links", fetch: false,
            ("link", $"Defect:{defectId}"), ("link", "Defect:not-a-guid"), ("link", $"Nope:{defectId}"), ("link", "garbage"));
        Assert.Equal($"/projects/{projectId}/board/{storyId}#links", linked.Headers.Location?.OriginalString);

        await using var scope = app.Services.CreateAsyncScope();
        var board = scope.ServiceProvider.GetRequiredService<BoardService>();
        var links = await board.ListLinksAsync(storyId);
        var link = Assert.Single(links);
        Assert.Equal(BoardLinkKind.Defect, link.Kind);
        Assert.Equal("D-1", link.Ref);

        await PostAsync("viewer", $"/projects/{projectId}/board/{storyId}/links/{link.Id}/delete", fetch: false);
        Assert.Single(await board.ListLinksAsync(storyId));
        await PostAsync("dev", $"/projects/{projectId}/board/{storyId}/links/{link.Id}/delete", fetch: false);
        Assert.Empty(await board.ListLinksAsync(storyId));
    }

    [Fact]
    public async Task Only_the_author_or_a_qa_deletes_a_comment()
    {
        Guid commentId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            commentId = await scope.ServiceProvider.GetRequiredService<BoardService>()
                .AddCommentAsync(projectId, storyId, "dev", "Started on this", []);
        }

        async Task<int> CountAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<BoardService>().ListCommentsAsync(storyId)).Count;
        }

        var refused = await PostAsync("viewer", $"/projects/{projectId}/board/{storyId}/comments/{commentId}/delete", fetch: false,
            ("commentsPage", "2"));
        Assert.Equal($"/projects/{projectId}/board/{storyId}?commentsPage=2#comments", refused.Headers.Location?.OriginalString);
        Assert.Equal(1, await CountAsync());

        var deleted = await PostAsync("dev", $"/projects/{projectId}/board/{storyId}/comments/{commentId}/delete", fetch: false,
            ("commentsPage", ""));
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(0, await CountAsync());
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
