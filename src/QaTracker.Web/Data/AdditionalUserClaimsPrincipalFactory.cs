using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace QaTracker.Web.Data;

/// <summary>Adds application-specific claims (full name) to the signed-in principal.</summary>
public sealed class AdditionalUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    public const string ThemeClaimType = "qatracker:theme";
    public const string CurrentProjectClaimType = "qatracker:project";

    public override async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
    {
        var principal = await base.CreateAsync(user);

        if (principal.Identity is ClaimsIdentity identity)
        {
            if (!string.IsNullOrWhiteSpace(user.FullName))
            {
                identity.AddClaim(new Claim("FullName", user.FullName));
            }

            identity.AddClaim(new Claim(ThemeClaimType, user.Theme.ToString()));

            if (user.CurrentProjectId is { } projectId)
            {
                identity.AddClaim(new Claim(CurrentProjectClaimType, projectId.ToString()));
            }
        }

        return principal;
    }
}
