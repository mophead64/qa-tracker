using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using QaTracker.Web.Data;

namespace QaTracker.Web.Auth;

/// <summary>
/// Keeps a user's role in lock-step with the roles carried by their identity provider's
/// token. Run on every external sign-in — the provider is the source of truth, so a role it
/// no longer asserts is removed in-app. A user holds exactly one role; if the token carries
/// both QA and Dev values, QA (the privileged one) wins.
/// </summary>
public sealed class ExternalRoleSynchronizer(
    UserManager<ApplicationUser> userManager,
    OidcAuthSettings settings)
{
    /// <summary>
    /// Reconciles <paramref name="user"/>'s single role against the role claims in
    /// <paramref name="externalPrincipal"/>. QA wins if the token asserts both; a token with
    /// no recognised role value leaves the user with no role.
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
    }
}
