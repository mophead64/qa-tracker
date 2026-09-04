using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;

namespace QaTracker.UnitTests.Defects;

public sealed class UserDirectoryTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;

    public UserDirectoryTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        factory = new TestDbContextFactory(options);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        db.Roles.AddRange(
            new IdentityRole("QA") { Id = "role-qa", NormalizedName = "QA" },
            new IdentityRole("Dev") { Id = "role-dev", NormalizedName = "DEV" });
        db.Users.AddRange(
            new ApplicationUser { Id = "u-qa", UserName = "qa@test.local", FullName = "Alice QA" },
            new ApplicationUser { Id = "u-dev", UserName = "dev@test.local", FullName = "Bob Dev" },
            new ApplicationUser { Id = "u-none", UserName = "nobody@test.local" });
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = "u-qa", RoleId = "role-qa" },
            new IdentityUserRole<string> { UserId = "u-dev", RoleId = "role-dev" });
        db.SaveChanges();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task ListAssignableAsync_returns_users_with_role_ordered_by_name()
    {
        var sut = new UserDirectory(factory);

        var users = await sut.ListAssignableAsync();

        Assert.Equal(["Alice QA", "Bob Dev", "nobody@test.local"], users.Select(u => u.DisplayName));
        Assert.Equal("QA", users.Single(u => u.Id == "u-qa").Role);
        Assert.Equal("Dev", users.Single(u => u.Id == "u-dev").Role);
        Assert.Null(users.Single(u => u.Id == "u-none").Role);
    }

    [Fact]
    public async Task UserOption_label_prefixes_role_when_known()
    {
        var sut = new UserDirectory(factory);

        var users = await sut.ListAssignableAsync();

        Assert.Equal("[QA] Alice QA", users.Single(u => u.Id == "u-qa").Label);
        Assert.Equal("nobody@test.local", users.Single(u => u.Id == "u-none").Label);
    }

    [Fact]
    public async Task GetAsync_resolves_a_single_user()
    {
        var sut = new UserDirectory(factory);

        var user = await sut.GetAsync("u-dev");

        Assert.Equal("Bob Dev", user!.DisplayName);
        Assert.Equal("Dev", user.Role);
    }

    public void Dispose() => connection.Dispose();
}
