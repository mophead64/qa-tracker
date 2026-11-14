using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Auth;
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
/// <see cref="Defects.UserDirectory"/>. With local sign-in disabled, accounts can't be
/// created or given passwords here — SSO provisions them on first sign-in.
/// </summary>
public sealed class UserService(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    LocalAuthSettings localAuth)
{
    /// <summary>False when local sign-in is disabled; accounts then come only from SSO.</summary>
    public bool CanCreateUsers => localAuth.Enabled;

    public async Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.DeletedUtc == null)
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
        if (!localAuth.Enabled)
        {
            return UserOperationResult.Failure(
                ["Local accounts are disabled. Users are created automatically on their first SSO sign-in."]);
        }

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

        if (!localAuth.Enabled && !string.IsNullOrEmpty(newPassword))
        {
            return UserOperationResult.Failure(["Local sign-in is disabled, so passwords can't be set."]);
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

    /// <summary>
    /// Deletes an account. Their defects go back to unassigned, they leave every project team and
    /// their notifications go. An account with nothing else attached is then removed; one that
    /// created content or recorded fixes / results is kept as an anonymous "Deleted user"
    /// (<see cref="ApplicationUser.DeletedUtc"/>) so that history stays whole: no email, password,
    /// SSO link or role — it can't sign in — and it's left out of every list and picker.
    /// </summary>
    public async Task<UserOperationResult> DeleteAsync(string id, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null || user.DeletedUtc is not null)
        {
            return UserOperationResult.Success;
        }

        bool hasHistory;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await db.Defects
                .Where(d => d.AssignedToId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.AssignedToId, (string?)null), ct);
            await db.Notifications.Where(n => n.UserId == id).ExecuteDeleteAsync(ct);

            var teams = await db.Projects.Include(p => p.Members).Where(p => p.Members.Any(m => m.Id == id)).ToListAsync(ct);
            foreach (var project in teams)
            {
                project.Members.RemoveAll(m => m.Id == id);
            }
            await db.SaveChangesAsync(ct);

            hasHistory = await HasHistoryAsync(db, id, ct);
        }

        if (!hasHistory)
        {
            var result = await userManager.DeleteAsync(user);
            return result.Succeeded
                ? UserOperationResult.Success
                : UserOperationResult.Failure(result.Errors.Select(e => e.Description));
        }

        return await TombstoneAsync(user);
    }

    /// <summary>Whether anything still points at the user that can't simply go with them.</summary>
    private static async Task<bool> HasHistoryAsync(ApplicationDbContext db, string id, CancellationToken ct) =>
        await db.Projects.AnyAsync(p => p.CreatedById == id, ct)
        || await db.TestScopes.AnyAsync(s => s.CreatedById == id, ct)
        || await db.TestCases.AnyAsync(tc => tc.CreatedById == id || tc.TestedById == id, ct)
        || await db.Defects.AnyAsync(d => d.CreatedById == id || d.FixedById == id || d.TestedById == id, ct)
        || await db.TestCaseComments.AnyAsync(c => c.AuthorId == id, ct)
        || await db.DefectComments.AnyAsync(c => c.AuthorId == id, ct)
        || await db.ProjectComments.AnyAsync(c => c.AuthorId == id, ct)
        || await db.Attachments.AnyAsync(a => a.UploadedById == id, ct);

    private async Task<UserOperationResult> TombstoneAsync(ApplicationUser user)
    {
        foreach (var login in await userManager.GetLoginsAsync(user))
        {
            await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        }
        if (await userManager.HasPasswordAsync(user))
        {
            await userManager.RemovePasswordAsync(user);
        }
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(user, roles);
        }

        // A placeholder address keeps the real one free for a new account.
        var placeholder = $"deleted-{user.Id}@deleted.invalid";
        user.FullName = ApplicationUser.DeletedName;
        user.UserName = placeholder;
        user.Email = placeholder;
        user.EmailConfirmed = false;
        user.ExternalProvider = null;
        user.CurrentProjectId = null;
        user.DeletedUtc = DateTimeOffset.UtcNow;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        await userManager.UpdateNormalizedUserNameAsync(user);
        await userManager.UpdateNormalizedEmailAsync(user);

        var update = await userManager.UpdateAsync(user);
        if (update.Succeeded)
        {
            // Signs out any session still open.
            await userManager.UpdateSecurityStampAsync(user);
        }

        return update.Succeeded
            ? UserOperationResult.Success
            : UserOperationResult.Failure(update.Errors.Select(e => e.Description));
    }
}
