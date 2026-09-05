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

        await new ExternalRoleSynchronizer(UserManager, Settings).SyncAsync(user, PrincipalWithRoles("QA"));

        Assert.Equal([Roles.QA], await UserManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task Switches_roles_to_match_the_token()
    {
        var user = await CreateUserAsync(Roles.QA);

        await new ExternalRoleSynchronizer(UserManager, Settings).SyncAsync(user, PrincipalWithRoles("Dev"));

        Assert.Equal([Roles.Dev], await UserManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task Grants_both_roles_when_the_token_carries_both()
    {
        var user = await CreateUserAsync();

        await new ExternalRoleSynchronizer(UserManager, Settings).SyncAsync(user, PrincipalWithRoles("QA", "Dev"));

        var roles = await UserManager.GetRolesAsync(user);
        Assert.Contains(Roles.QA, roles);
        Assert.Contains(Roles.Dev, roles);
    }

    [Fact]
    public async Task Removes_all_roles_when_the_token_has_no_recognised_value()
    {
        var user = await CreateUserAsync(Roles.QA, Roles.Dev);

        await new ExternalRoleSynchronizer(UserManager, Settings).SyncAsync(user, PrincipalWithRoles("some-other-group"));

        Assert.Empty(await UserManager.GetRolesAsync(user));
    }

    public void Dispose()
    {
        serviceProvider.Dispose();
        connection.Dispose();
    }
}
