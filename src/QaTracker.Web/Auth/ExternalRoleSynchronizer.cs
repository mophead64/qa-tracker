using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Admin;
using QaTracker.Web.Data;

namespace QaTracker.Web.Auth;

/// <summary>
/// Keeps a user's role in lock-step with the roles carried by their identity provider's
/// token. Run on every external sign-in — the provider is the source of truth, so a role it
/// no longer asserts is removed in-app. A user holds exactly one role; if the token carries
/// several, the most privileged wins (QA, then Dev, then Viewer). Becoming a Viewer takes the
/// user off project teams and their defects, as it does when an admin changes the role.
/// </summary>
public sealed class ExternalRoleSynchronizer(
    UserManager<ApplicationUser> userManager,
    OidcAuthSettings settings,
    IDbContextFactory<ApplicationDbContext> dbFactory)
{
    /// <summary>
    /// Reconciles <paramref name="user"/>'s single role against the role claims in
    /// <paramref name="externalPrincipal"/>. The most privileged recognised value wins; a token
    /// with no recognised role value leaves the user with no role.
    /// </summary>
    public async Task SyncAsync(ApplicationUser user, ClaimsPrincipal externalPrincipal)
    {
        var claimValues = externalPrincipal.FindAll(settings.RoleClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var desired = new List<string>();
        if (claimValues.Contains(settings.QaRoleValue))
        {
            desired.Add(Roles.QA);
        }
        else if (claimValues.Contains(settings.DevRoleValue))
        {
            desired.Add(Roles.Dev);
        }
        else if (claimValues.Contains(settings.ViewerRoleValue))
        {
            desired.Add(Roles.Viewer);
        }

        var current = await userManager.GetRolesAsync(user);

        var toRemove = current.Except(desired).ToList();
        var toAdd = desired.Except(current).ToList();

        if (toRemove.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(user, toRemove);
        }

        if (toAdd.Count > 0)
        {
            await userManager.AddToRolesAsync(user, toAdd);
        }

        if (toAdd.Contains(Roles.Viewer))
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await UserService.ReleaseFromWorkAsync(db, user.Id);
        }
    }
}
