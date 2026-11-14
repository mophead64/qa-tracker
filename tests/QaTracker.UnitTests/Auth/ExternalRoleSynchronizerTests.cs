using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;

namespace QaTracker.UnitTests.Auth;

public sealed class ExternalRoleSynchronizerTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly ServiceProvider serviceProvider;

    private static readonly OidcAuthSettings Settings = new(
        ProviderLabel: "Keycloak",
        Authority: "http://localhost/realms/x",
        MetadataAddress: null,
        ClientId: "c",
        ClientSecret: "s",
        Scopes: ["openid"],
        RequireHttpsMetadata: false,
        RoleClaimType: "roles",
        QaRoleValue: "QA",
        DevRoleValue: "Dev");

    public ExternalRoleSynchronizerTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<ApplicationDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        serviceProvider = services.BuildServiceProvider();

        using var db = serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            roleManager.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
        }
    }

    private ExternalRoleSynchronizer Sut => new(UserManager, Settings,
        serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>());

    private UserManager<ApplicationUser> UserManager => serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    private async Task<ApplicationUser> CreateUserAsync(params string[] roles)
    {
        var user = new ApplicationUser { UserName = "u@test.local", Email = "u@test.local", ExternalProvider = "oidc" };
        await UserManager.CreateAsync(user);
        if (roles.Length > 0)
        {
            await UserManager.AddToRolesAsync(user, roles);
        }

        return user;
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roleValues) =>
        new(new ClaimsIdentity(roleValues.Select(r => new Claim("roles", r))));

    [Fact]
    public async Task Adds_the_qa_role_from_the_token()
    {
        var user = await CreateUserAsync();

        await Sut.SyncAsync(user, PrincipalWithRoles("QA"));

        Assert.Equal([Roles.QA], await UserManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task Switches_roles_to_match_the_token()
    {
        var user = await CreateUserAsync(Roles.QA);

        await Sut.SyncAsync(user, PrincipalWithRoles("Dev"));

        Assert.Equal([Roles.Dev], await UserManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task Picks_QA_when_the_token_carries_both()
    {
        var user = await CreateUserAsync(Roles.Dev);

        await Sut.SyncAsync(user, PrincipalWithRoles("QA", "Dev"));

        Assert.Equal([Roles.QA], await UserManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task Removes_all_roles_when_the_token_has_no_recognised_value()
    {
        var user = await CreateUserAsync(Roles.QA, Roles.Dev);

        await Sut.SyncAsync(user, PrincipalWithRoles("some-other-group"));

        Assert.Empty(await UserManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task Maps_the_viewer_value_and_prefers_dev_over_viewer()
    {
        var viewer = await CreateUserAsync();
        await Sut.SyncAsync(viewer, PrincipalWithRoles("Viewer"));
        Assert.Equal([Roles.Viewer], await UserManager.GetRolesAsync(viewer));

        await Sut.SyncAsync(viewer, PrincipalWithRoles("Viewer", "Dev"));
        Assert.Equal([Roles.Dev], await UserManager.GetRolesAsync(viewer));
    }

    [Fact]
    public async Task Becoming_a_viewer_takes_the_user_off_teams_and_defects()
    {
        var user = await CreateUserAsync(Roles.Dev);
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var projectId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id);
            db.Projects.Add(new Web.Projects.Project { Id = projectId, Name = "P", CreatedById = user.Id, Members = [tracked] });
            db.Defects.Add(new Web.Defects.Defect
            {
                Id = Guid.NewGuid(), ProjectId = projectId, Number = 1, Summary = "Bug", CreatedById = user.Id, AssignedToId = user.Id,
            });
            await db.SaveChangesAsync();
        }

        await Sut.SyncAsync(user, PrincipalWithRoles("Viewer"));

        await using var check = await factory.CreateDbContextAsync();
        Assert.False(await check.Projects.AnyAsync(p => p.Members.Any(m => m.Id == user.Id)));
        Assert.Null((await check.Defects.SingleAsync()).AssignedToId);
    }

    public void Dispose()
    {
        serviceProvider.Dispose();
        connection.Dispose();
    }
}
