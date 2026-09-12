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
    public const string NotificationSoundClaimType = "qatracker:notif-sound";
    public const string NotificationSoundEnabledClaimType = "qatracker:notif-sound-enabled";

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
            identity.AddClaim(new Claim(NotificationSoundClaimType, user.NotificationSound));
            identity.AddClaim(new Claim(NotificationSoundEnabledClaimType, user.NotificationSoundEnabled.ToString()));

            if (user.CurrentProjectId is { } projectId)
            {
                identity.AddClaim(new Claim(CurrentProjectClaimType, projectId.ToString()));
            }
        }

        return principal;
    }
}
