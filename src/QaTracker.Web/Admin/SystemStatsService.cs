using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.Admin;

/// <summary>Headline counts shown on the system settings page.</summary>
public sealed record SystemStats(int Projects, int TestCases, int Defects, int Files, long FileBytes);

/// <summary>
/// Computes the system-wide usage counts for the system settings dashboard. Read-only, so
/// it queries the context factory directly rather than going through the per-aggregate
/// services (mirrors <see cref="Dashboard.ProjectActionsService"/>).
/// </summary>
public sealed class SystemStatsService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<SystemStats> GetAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return new SystemStats(
            await db.Projects.CountAsync(ct),
            await db.TestCases.CountAsync(ct),
            await db.Defects.CountAsync(ct),
            await db.Attachments.CountAsync(ct),
            await db.Attachments.SumAsync(a => (long?)a.SizeBytes, ct) ?? 0);
    }
}
