using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QaTracker.Web.Admin;
using QaTracker.Web.Data;

namespace QaTracker.UnitTests.Admin;

public sealed class SystemSettingsServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;

    public SystemSettingsServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        factory = new TestDbContextFactory(options);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private SystemSettingsService CreateSut() => new(factory, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetAsync_defaults_every_area_to_allowed_for_developers()
    {
        var sut = CreateSut();

        var settings = await sut.GetAsync();

        Assert.True(settings.DevelopersCanManageProjects);
        Assert.True(settings.DevelopersCanManageTestCases);
        Assert.True(settings.DevelopersCanManageDefects);
    }

    [Fact]
    public async Task UpdateAsync_persists_and_is_visible_to_a_fresh_service()
    {
        await CreateSut().UpdateAsync(new SystemSettingsView(
            DevelopersCanManageProjects: false,
            DevelopersCanManageTestCases: true,
            DevelopersCanManageDefects: false));

        var reloaded = await CreateSut().GetViewAsync();

        Assert.False(reloaded.DevelopersCanManageProjects);
        Assert.True(reloaded.DevelopersCanManageTestCases);
        Assert.False(reloaded.DevelopersCanManageDefects);
    }

    [Fact]
    public async Task UpdateAsync_evicts_the_cache_so_the_same_instance_sees_the_change()
    {
        var sut = CreateSut();
        Assert.True(await sut.DevelopersCanManageAsync(ManageableArea.Defects));

        await sut.UpdateAsync(new SystemSettingsView(true, true, DevelopersCanManageDefects: false));

        Assert.False(await sut.DevelopersCanManageAsync(ManageableArea.Defects));
    }

    [Fact]
    public async Task GetAsync_never_creates_a_duplicate_row()
    {
        await CreateSut().GetAsync();
        await CreateSut().GetAsync();

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.SystemSettings.CountAsync());
    }

    public void Dispose() => connection.Dispose();
}
