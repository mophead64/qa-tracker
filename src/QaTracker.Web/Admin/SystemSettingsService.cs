using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QaTracker.Web.Data;

namespace QaTracker.Web.Admin;

/// <summary>Editable copy of the developer-permission flags for the system settings form.</summary>
public sealed record SystemSettingsView(
    bool DevelopersCanManageProjects,
    bool DevelopersCanManageTestCases,
    bool DevelopersCanManageDefects);

/// <summary>
/// Reads and updates the single <see cref="SystemSettings"/> row. Reads are cached for a
/// few seconds because the authorization handler consults them on every protected
/// navigation; a write clears the cache, and other instances pick the change up once the
/// entry expires.
/// </summary>
public sealed class SystemSettingsService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IMemoryCache cache)
{
    private const string CacheKey = "system-settings";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    public async Task<SystemSettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out SystemSettings? cached) && cached is not null)
        {
            return cached;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await LoadOrCreateAsync(db, ct);
        cache.Set(CacheKey, settings, CacheDuration);
        return settings;
    }

    public async Task<SystemSettingsView> GetViewAsync(CancellationToken ct = default)
    {
        var s = await GetAsync(ct);
        return new SystemSettingsView(
            s.DevelopersCanManageProjects,
            s.DevelopersCanManageTestCases,
            s.DevelopersCanManageDefects);
    }

    public async Task<bool> DevelopersCanManageAsync(ManageableArea area, CancellationToken ct = default) =>
        (await GetAsync(ct)).AllowsDeveloperManagementOf(area);

    public async Task UpdateAsync(SystemSettingsView values, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await LoadOrCreateAsync(db, ct);

        settings.DevelopersCanManageProjects = values.DevelopersCanManageProjects;
        settings.DevelopersCanManageTestCases = values.DevelopersCanManageTestCases;
        settings.DevelopersCanManageDefects = values.DevelopersCanManageDefects;

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
    }

    // The row is seeded by migration, but tolerate its absence (older databases, tests that
    // only EnsureCreated the schema) by materialising the default on first access.
    private static async Task<SystemSettings> LoadOrCreateAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var settings = await db.SystemSettings.FirstOrDefaultAsync(s => s.Id == SystemSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new SystemSettings { Id = SystemSettings.SingletonId };
            db.SystemSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        return settings;
    }
}
