using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    // These endpoints are required by the Identity Razor components defined in the /Components/Account/Pages directory of this project.
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        // Kick off an external (OIDC) sign-in. Only reachable when an auth provider is
        // configured; the login page hides the button otherwise.
        accountGroup.MapPost("/PerformExternalLogin", (
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string? returnUrl) =>
        {
            var redirectUrl = $"/Account/ExternalLogin?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";

            // Stamps the "LoginProvider" property that SignInManager.GetExternalLoginInfoAsync
            // reads back on the callback.
            var properties = signInManager.ConfigureExternalAuthenticationProperties(
                AuthConfig.OidcScheme, redirectUrl);

            return TypedResults.Challenge(properties, [AuthConfig.OidcScheme]);
        });

        accountGroup.MapPost("/Logout", async (
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            // Local sign-out only. The provider's own SSO session is left intact — the next
            // "Sign in with …" click re-authenticates silently, which is the expected SSO
            // behaviour; Login.razor clears the stale external cookie on GET.
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        return accountGroup;
    }
}
