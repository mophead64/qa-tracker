using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QaTracker.Web.Admin;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;

namespace QaTracker.UnitTests.Auth;

public sealed class ManageAreaHandlerTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;

    public ManageAreaHandlerTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        factory = new TestDbContextFactory(options);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private SystemSettingsService Settings => new(factory, new MemoryCache(new MemoryCacheOptions()));

    private async Task<bool> Evaluate(ClaimsPrincipal user, ManageableArea area)
    {
        var requirement = new ManageAreaRequirement(area);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new ManageAreaHandler(Settings).HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal UserInRoles(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), authenticationType: "Test"));

    [Fact]
    public async Task Qa_can_always_manage_every_area()
    {
        await Settings.UpdateAsync(new SystemSettingsView(false, false, false));
        var qa = UserInRoles(Roles.QA);

        Assert.True(await Evaluate(qa, ManageableArea.Projects));
        Assert.True(await Evaluate(qa, ManageableArea.TestCases));
        Assert.True(await Evaluate(qa, ManageableArea.Defects));
    }

    [Fact]
    public async Task Dev_can_manage_an_area_only_while_the_setting_allows_it()
    {
        var dev = UserInRoles(Roles.Dev);
        Assert.True(await Evaluate(dev, ManageableArea.Projects));

        await Settings.UpdateAsync(new SystemSettingsView(
            DevelopersCanManageProjects: false,
            DevelopersCanManageTestCases: true,
            DevelopersCanManageDefects: true));

        Assert.False(await Evaluate(dev, ManageableArea.Projects));
        Assert.True(await Evaluate(dev, ManageableArea.TestCases));
    }

    [Fact]
    public async Task A_user_with_no_relevant_role_is_never_granted()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(await Evaluate(anonymous, ManageableArea.Defects));
    }

    public void Dispose() => connection.Dispose();
}
