using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.Admin;

/// <summary>One row of the system user list, with roles resolved.</summary>
public sealed record AdminUser(string Id, string Email, string? FullName, IReadOnlyList<string> Roles);

/// <summary>Result of a create/update/delete attempt that can fail with Identity validation errors.</summary>
public sealed record UserOperationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static UserOperationResult Success { get; } = new(true, []);

    public static UserOperationResult Failure(IEnumerable<string> errors) => new(false, errors.ToList());
}

/// <summary>
/// CRUD over <see cref="ApplicationUser"/> for the system settings area. A thin wrapper
/// around <see cref="UserManager{TUser}"/> (which already handles password hashing and
/// validation), plus a context factory for read-only listing — mirrors
/// <see cref="Defects.UserDirectory"/>.
/// </summary>
public sealed class UserService(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.Email, u.FullName })
            .ToListAsync(ct);

        var roles = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, RoleName = r.Name! })
            .ToListAsync(ct);

        var rolesByUser = roles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.RoleName).OrderBy(r => r).ToList());

        return users
            .Select(u => new AdminUser(u.Id, u.Email ?? "", u.FullName, rolesByUser.GetValueOrDefault(u.Id, [])))
            .OrderBy(u => u.FullName ?? u.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AdminUser?> GetAsync(string id, CancellationToken ct = default) =>
        (await ListAsync(ct)).FirstOrDefault(u => u.Id == id);

    public async Task<UserOperationResult> CreateAsync(
        string email, string? fullName, string password, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true,
            FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim(),
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return UserOperationResult.Failure(result.Errors.Select(e => e.Description));
        }

        if (roles.Count > 0)
        {
            await userManager.AddToRolesAsync(user, roles);
        }

        return UserOperationResult.Success;
    }

    public async Task<UserOperationResult> UpdateAsync(
        string id, string? fullName, IReadOnlyList<string> roles, string? newPassword, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserOperationResult.Failure(["User not found."]);
        }

        user.FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return UserOperationResult.Failure(updateResult.Errors.Select(e => e.Description));
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Except(roles).ToList();
        var toAdd = roles.Except(currentRoles).ToList();
        if (toRemove.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(user, toRemove);
        }

        if (toAdd.Count > 0)
        {
            await userManager.AddToRolesAsync(user, toAdd);
        }

        if (!string.IsNullOrEmpty(newPassword))
        {
            await userManager.RemovePasswordAsync(user);
            var passwordResult = await userManager.AddPasswordAsync(user, newPassword);
            if (!passwordResult.Succeeded)
            {
                return UserOperationResult.Failure(passwordResult.Errors.Select(e => e.Description));
            }
        }

        return UserOperationResult.Success;
    }

    public async Task<UserOperationResult> DeleteAsync(string id, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserOperationResult.Success;
        }

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded
            ? UserOperationResult.Success
            : UserOperationResult.Failure(result.Errors.Select(e => e.Description));
    }
}
