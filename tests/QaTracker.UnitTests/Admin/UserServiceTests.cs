using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Web.Admin;
using QaTracker.Web.Data;

namespace QaTracker.UnitTests.Admin;

public sealed class UserServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly ServiceProvider serviceProvider;
    private readonly IDbContextFactory<ApplicationDbContext> factory;

    public UserServiceTests()
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
        factory = serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            roleManager.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
        }
    }

    private UserService CreateSut() =>
        new(serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(), factory);

    [Fact]
    public async Task CreateAsync_creates_a_user_with_the_given_role()
    {
        var sut = CreateSut();

        var result = await sut.CreateAsync("jane@test.local", "Jane Doe", "Str0ng!Passw0rd", Roles.QA);

        Assert.True(result.Succeeded);
        var user = Assert.Single(await sut.ListAsync());
        Assert.Equal("jane@test.local", user.Email);
        Assert.Equal("Jane Doe", user.FullName);
        Assert.Equal(Roles.QA, user.Role);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_role()
    {
        var result = await CreateSut().CreateAsync("jane@test.local", "Jane Doe", "Str0ng!Passw0rd", "Admin");

        Assert.False(result.Succeeded);
        Assert.Empty(await CreateSut().ListAsync());
    }

    [Fact]
    public async Task CreateAsync_fails_with_identity_errors_for_a_weak_password()
    {
        var sut = CreateSut();

        var result = await sut.CreateAsync("jane@test.local", "Jane Doe", "a", null);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(await sut.ListAsync());
    }

    [Fact]
    public async Task UpdateAsync_changes_full_name_and_replaces_the_role()
    {
        var sut = CreateSut();
        await sut.CreateAsync("jane@test.local", "Jane Doe", "Str0ng!Passw0rd", Roles.QA);
        var created = Assert.Single(await sut.ListAsync());

        var result = await sut.UpdateAsync(created.Id, "Jane R. Doe", Roles.Dev, null);

        Assert.True(result.Succeeded);
        var updated = Assert.Single(await sut.ListAsync());
        Assert.Equal("Jane R. Doe", updated.FullName);
        Assert.Equal(Roles.Dev, updated.Role);
    }

    [Fact]
    public async Task UpdateAsync_with_a_new_password_lets_the_user_sign_in_with_it()
    {
        var sut = CreateSut();
        await sut.CreateAsync("jane@test.local", "Jane Doe", "Str0ng!Passw0rd", Roles.Dev);
        var created = Assert.Single(await sut.ListAsync());

        var result = await sut.UpdateAsync(created.Id, "Jane Doe", Roles.Dev, "N3w!Passw0rd");

        Assert.True(result.Succeeded);
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(created.Id);
        Assert.True(await userManager.CheckPasswordAsync(user!, "N3w!Passw0rd"));
    }

    [Fact]
    public async Task ListAsync_surfaces_the_external_provider()
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await userManager.CreateAsync(new ApplicationUser
        {
            UserName = "sso@test.local",
            Email = "sso@test.local",
            ExternalProvider = "oidc",
        });

        var user = Assert.Single(await CreateSut().ListAsync());
        Assert.Equal("oidc", user.ExternalProvider);
        Assert.True(user.IsExternallyManaged);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_externally_managed_user()
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = "sso@test.local",
            Email = "sso@test.local",
            FullName = "SSO User",
            ExternalProvider = "oidc",
        };
        await userManager.CreateAsync(user);
        await userManager.AddToRoleAsync(user, Roles.Dev);

        var result = await CreateSut().UpdateAsync(user.Id, "Changed Name", Roles.QA, "N3w!Passw0rd");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        var reloaded = Assert.Single(await CreateSut().ListAsync());
        Assert.Equal("SSO User", reloaded.FullName);
        Assert.Equal(Roles.Dev, reloaded.Role);
    }

    [Fact]
    public async Task ListAsync_picks_QA_when_a_user_somehow_has_both_roles()
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "both@test.local", Email = "both@test.local" };
        await userManager.CreateAsync(user);
        await userManager.AddToRolesAsync(user, [Roles.QA, Roles.Dev]);

        Assert.Equal(Roles.QA, Assert.Single(await CreateSut().ListAsync()).Role);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_user()
    {
        var sut = CreateSut();
        await sut.CreateAsync("jane@test.local", "Jane Doe", "Str0ng!Passw0rd", Roles.QA);
        var created = Assert.Single(await sut.ListAsync());

        var result = await sut.DeleteAsync(created.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(await sut.ListAsync());
    }

    public void Dispose()
    {
        serviceProvider.Dispose();
        connection.Dispose();
    }
}
