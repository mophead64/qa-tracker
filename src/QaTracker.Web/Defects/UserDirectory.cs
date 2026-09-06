using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.Defects;

/// <summary>One selectable user, with their role for display (e.g. "Jane Doe (Dev)").</summary>
public sealed record UserOption(string Id, string DisplayName, string? Role)
{
    /// <summary>"Name (Role)" when a role is known, otherwise just the name — for plain
    /// &lt;option&gt; text where a <c>RoleBadge</c> can't render.</summary>
    public string Label => Role is null ? DisplayName : $"{DisplayName} ({Role})";
}

/// <summary>
/// Lists users for pickers: <see cref="ListAssignableAsync"/> is every user (the team-add
/// modal), <see cref="ListForProjectAsync"/> is just a project's team members (the defect
/// "Assigned to" picker — a defect can only be assigned to someone on its project's team).
/// Uses a context factory so each call gets a short-lived context.
/// </summary>
public sealed class UserDirectory(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<IReadOnlyList<UserOption>> ListAssignableAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var users = await db.Users
            .AsNoTracking()
            .Select(u => new UserRow(u.Id, u.FullName, u.UserName))
            .ToListAsync(ct);

        return await ResolveAsync(db, users, ct);
    }

    /// <summary>The project's team members, ordered by name. Empty when the project has no team.</summary>
    public async Task<IReadOnlyList<UserOption>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var users = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Members)
            .Select(u => new UserRow(u.Id, u.FullName, u.UserName))
            .ToListAsync(ct);

        return await ResolveAsync(db, users, ct);
    }

    public async Task<UserOption?> GetAsync(string userId, CancellationToken ct = default)
    {
        var all = await ListAssignableAsync(ct);
        return all.FirstOrDefault(o => o.Id == userId);
    }

    private static async Task<IReadOnlyList<UserOption>> ResolveAsync(
        ApplicationDbContext db, List<UserRow> users, CancellationToken ct)
    {
        var ids = users.Select(u => u.Id).ToHashSet();
        var roles = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId)
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

    private sealed record UserRow(string Id, string? FullName, string? UserName);
}
