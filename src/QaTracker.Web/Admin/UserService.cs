using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.Admin;

/// <summary>One row of the system user list. A user has exactly one role (QA or Dev), or
/// none yet — never both.</summary>
public sealed record AdminUser(
    string Id, string Email, string? FullName, string? Role, string? ExternalProvider,
    DateTimeOffset? LastLoginUtc)
{
    /// <summary>True when an external identity provider owns this account.</summary>
    public bool IsExternallyManaged => ExternalProvider is not null;

    /// <summary>Last sign-in as "yyyy-MM-dd HH:mm UTC", or "Never".</summary>
    public string LastLoginDisplay => LastLoginUtc is { } t
        ? t.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
        : "Never";
}

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
            .Select(u => new { u.Id, u.Email, u.FullName, u.ExternalProvider, u.LastLoginUtc })
            .ToListAsync(ct);

        var roles = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, RoleName = r.Name! })
            .ToListAsync(ct);

        // A user should carry one role; if a stale row leaves two, QA (the privileged one) wins.
        var roleByUser = roles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Any(x => x.RoleName == Roles.QA) ? Roles.QA : g.First().RoleName);

        return users
            .Select(u => new AdminUser(
                u.Id, u.Email ?? "", u.FullName, roleByUser.GetValueOrDefault(u.Id), u.ExternalProvider,
                u.LastLoginUtc))
            .OrderBy(u => u.FullName ?? u.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AdminUser?> GetAsync(string id, CancellationToken ct = default) =>
        (await ListAsync(ct)).FirstOrDefault(u => u.Id == id);

    public async Task<UserOperationResult> CreateAsync(
        string email, string? fullName, string password, string? role, CancellationToken ct = default)
    {
        if (role is not null && !Roles.All.Contains(role))
        {
            return UserOperationResult.Failure(["Role must be QA or Dev."]);
        }

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

        if (role is not null)
        {
            await userManager.AddToRoleAsync(user, role);
        }

        return UserOperationResult.Success;
    }

    public async Task<UserOperationResult> UpdateAsync(
        string id, string? fullName, string? role, string? newPassword, CancellationToken ct = default)
    {
        if (role is not null && !Roles.All.Contains(role))
        {
            return UserOperationResult.Failure(["Role must be QA or Dev."]);
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserOperationResult.Failure(["User not found."]);
        }

        if (user.ExternalProvider is not null)
        {
            return UserOperationResult.Failure(
                ["This account is managed by an external identity provider. Its name, password and roles are controlled there."]);
        }

        user.FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return UserOperationResult.Failure(updateResult.Errors.Select(e => e.Description));
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var desired = role is null ? Array.Empty<string>() : [role];
        var toRemove = currentRoles.Except(desired).ToList();
        var toAdd = desired.Except(currentRoles).ToList();
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

    /// <summary>
    /// Repoints an SSO-managed account's email address (and username, which mirrors it).
    /// The provider link is keyed on the subject id, not the email, so this never breaks
    /// sign-in — but the in-app email should always match the identity provider's, so this
    /// is only meant to reflect a change already made there. Rejected for local accounts.
    /// </summary>
    public async Task<UserOperationResult> ChangeExternalEmailAsync(
        string id, string? newEmail, CancellationToken ct = default)
    {
        var trimmed = newEmail?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return UserOperationResult.Failure(["Enter the new email address."]);
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserOperationResult.Failure(["User not found."]);
        }

        if (user.ExternalProvider is null)
        {
            return UserOperationResult.Failure(
                ["This workflow is only for accounts managed by an external identity provider."]);
        }

        if (string.Equals(user.Email, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return UserOperationResult.Failure(["That is already this user's email address."]);
        }

        var clash = await userManager.FindByEmailAsync(trimmed);
        if (clash is not null && clash.Id != user.Id)
        {
            return UserOperationResult.Failure(["Another account already uses that email address."]);
        }

        var setUserName = await userManager.SetUserNameAsync(user, trimmed);
        if (!setUserName.Succeeded)
        {
            return UserOperationResult.Failure(setUserName.Errors.Select(e => e.Description));
        }

        var setEmail = await userManager.SetEmailAsync(user, trimmed);
        if (!setEmail.Succeeded)
        {
            return UserOperationResult.Failure(setEmail.Errors.Select(e => e.Description));
        }

        // SetEmailAsync clears the confirmed flag; a provider-managed account is always confirmed.
        user.EmailConfirmed = true;
        var update = await userManager.UpdateAsync(user);
        return update.Succeeded
            ? UserOperationResult.Success
            : UserOperationResult.Failure(update.Errors.Select(e => e.Description));
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
