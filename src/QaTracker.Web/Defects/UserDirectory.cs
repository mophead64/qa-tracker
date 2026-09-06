using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.Defects;

/// <summary>One selectable user, with their role for display (e.g. "[Dev] Jane Doe").</summary>
public sealed record UserOption(string Id, string DisplayName, string? Role)
{
    /// <summary>"[Role] Name" when a role is known, otherwise just the name.</summary>
    public string Label => Role is null ? DisplayName : $"[{Role}] {DisplayName}";
}

/// <summary>
/// Lists users for the defect "Assigned to" picker. The picker isn't scoped to a project's
/// team — every user is assignable. Uses a context factory so each call gets a short-lived
/// context.
/// </summary>
public sealed class UserDirectory(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<IReadOnlyList<UserOption>> ListAssignableAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(ct);

        var roles = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, RoleName = r.Name })
            .ToListAsync(ct);

        var roleByUser = roles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.RoleName).First().RoleName);

        return users
            .Select(u => new UserOption(
                u.Id,
                !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName! : u.UserName ?? "Unknown",
                roleByUser.GetValueOrDefault(u.Id)))
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<UserOption?> GetAsync(string userId, CancellationToken ct = default)
    {
        var all = await ListAssignableAsync(ct);
        return all.FirstOrDefault(o => o.Id == userId);
    }
}
