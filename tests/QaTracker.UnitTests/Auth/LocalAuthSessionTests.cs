using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;

namespace QaTracker.UnitTests.Auth;

public class LocalAuthSessionTests
{
    // Identity's own security-stamp check runs first inside OnValidatePrincipal; stub it out
    // so these tests exercise only the local-session gate.
    private sealed class PassThroughStampValidator : ISecurityStampValidator
    {
        public Task ValidateAsync(CookieValidatePrincipalContext context) => Task.CompletedTask;
    }

    private static async Task<ClaimsPrincipal?> ValidateAsync(bool localAuthEnabled, params Claim[] claims)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["QATRACKER_AUTH_PROVIDER"] = "Keycloak",
            ["QATRACKER_OIDC_AUTHORITY"] = "http://localhost:8081/realms/qatracker",
            ["QATRACKER_OIDC_CLIENT_ID"] = "qatracker-web",
            ["QATRACKER_OIDC_CLIENT_SECRET"] = "s3cret",
            ["QATRACKER_LOCAL_AUTH_ENABLED"] = localAuthEnabled.ToString(),
        });
        builder.AddAppAuthentication();
        builder.Services.AddSingleton<ISecurityStampValidator, PassThroughStampValidator>();

        using var app = builder.Build();
        var scheme = IdentityConstants.ApplicationScheme;
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
        var context = new CookieValidatePrincipalContext(
            new DefaultHttpContext { RequestServices = app.Services },
            new AuthenticationScheme(scheme, null, typeof(CookieAuthenticationHandler)),
            options,
            new AuthenticationTicket(principal, scheme));

        await options.Events.ValidatePrincipal(context);
        return context.Principal;
    }

    [Fact]
    public async Task Local_account_session_is_rejected_when_local_auth_is_disabled()
    {
        Assert.Null(await ValidateAsync(localAuthEnabled: false, new Claim(ClaimTypes.NameIdentifier, "u1")));
    }

    [Fact]
    public async Task Sso_account_session_survives_when_local_auth_is_disabled()
    {
        var principal = await ValidateAsync(
            localAuthEnabled: false,
            new Claim(ClaimTypes.NameIdentifier, "u1"),
            new Claim(AdditionalUserClaimsPrincipalFactory.ExternalProviderClaimType, AuthConfig.OidcScheme));

        Assert.NotNull(principal);
    }

    [Fact]
    public async Task Local_account_session_is_kept_when_local_auth_is_enabled()
    {
        Assert.NotNull(await ValidateAsync(localAuthEnabled: true, new Claim(ClaimTypes.NameIdentifier, "u1")));
    }
}
