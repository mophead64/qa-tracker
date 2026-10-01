using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using QaTracker.Web.Data;
using QaTracker.Web.Hosting;

namespace QaTracker.Web.Auth;

public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Identity cookie schemes and, when <c>QATRACKER_AUTH_PROVIDER</c> is
    /// set, a generic OpenID Connect handler for the selected provider (Entra or Keycloak).
    /// The external provider is additive unless <c>QATRACKER_LOCAL_AUTH_ENABLED=false</c>
    /// makes it the only way in. Returns a one-line summary for the startup log.
    /// </summary>
    public static string AddAppAuthentication(this WebApplicationBuilder builder)
    {
        var provider = AuthConfig.ResolveProvider(builder.Configuration);
        builder.Services.AddSingleton(typeof(AuthProvider), provider);

        var localAuth = AuthConfig.ResolveLocalAuth(builder.Configuration, provider);
        builder.Services.AddSingleton(localAuth);

        var authBuilder = builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        authBuilder.AddIdentityCookies();

        if (provider == AuthProvider.None)
        {
            return builder.Configuration.GetValue("QATRACKER_LOCAL_AUTH_ENABLED", true)
                ? "local accounts only"
                : "local accounts only (QATRACKER_LOCAL_AUTH_ENABLED=false ignored — no QATRACKER_AUTH_PROVIDER set)";
        }

        if (!localAuth.Enabled)
        {
            RejectLocalSessions(builder.Services);
        }

        var settings = AuthConfig.ResolveOidc(builder.Configuration, provider);
        builder.Services.AddSingleton(settings);
        builder.Services.AddScoped<ExternalRoleSynchronizer>();

        // Does the sign-in round-trip run over plain HTTP? True when the IdP itself is HTTP
        // (the bundled Keycloak) or this app is served over HTTP in development with no TLS
        // proxy in front. On HTTP the framework's hardened cookie defaults (Secure +
        // SameSite=None + form_post) are silently dropped by the browser, so every callback
        // fails with "Correlation failed" / "'.AspNetCore.Correlation.<…>' cookie not found".
        var appExpectsHttps =
            builder.Configuration.GetValue("QATRACKER_HTTPS_REDIRECT", false)
            || ForwardedHeadersConfig.IsEnabled(builder.Configuration);
        var signInIsPlainHttp =
            settings.Authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || (builder.Environment.IsDevelopment() && !appExpectsHttps);

        authBuilder.AddOpenIdConnect(AuthConfig.OidcScheme, options =>
        {
            options.Authority = settings.Authority;
            if (settings.MetadataAddress is not null)
            {
                // Back-channel discovery only — the browser still uses Authority. Lets the
                // app reach an in-cluster IdP whose public issuer URL it can't resolve
                // (e.g. Keycloak in docker-compose).
                options.MetadataAddress = settings.MetadataAddress;
            }

            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.RequireHttpsMetadata = settings.RequireHttpsMetadata;

            options.ResponseType = "code";
            options.UsePkce = true;
            options.GetClaimsFromUserInfoEndpoint = true;

            // Keep raw JWT claim names (sub, name, roles, …) instead of the long
            // schemas.xmlsoap.org URIs, so the role-claim name the operator configures is
            // the one ExternalRoleSynchronizer actually sees.
            options.MapInboundClaims = false;

            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.CallbackPath = "/signin-oidc";

            // When the sign-in runs over plain HTTP, undo the framework's three
            // HTTPS-assuming defaults for the correlation and nonce cookies, all of which
            // otherwise end in "Correlation failed" on the callback:
            //   1. SecurePolicy = Always -> the cookies carry `Secure`, so a plain-HTTP
            //      browser never stores them. Fall back to SameAsRequest.
            //   2. SameSite = None -> which browsers also reject unless the cookie is
            //      Secure. Drop to Lax.
            //   3. response_mode = form_post -> the IdP returns the result by POSTing a
            //      self-submitting form to /signin-oidc, and a Lax cookie is not sent on a
            //      cross-origin POST. The query response mode makes the callback a
            //      top-level GET, which does carry the Lax cookie.
            // Production over HTTPS keeps the hardened defaults.
            if (signInIsPlainHttp)
            {
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
            }

            options.Scope.Clear();
            foreach (var scope in settings.Scopes)
            {
                options.Scope.Add(scope);
            }

            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = settings.RoleClaimType;

            options.Events = new OpenIdConnectEvents
            {
                // With inbound mapping off, `sub` stays `sub`; SignInManager.GetExternalLoginInfoAsync
                // needs a NameIdentifier claim for the provider key.
                OnTokenValidated = context =>
                {
                    if (context.Principal?.Identity is ClaimsIdentity identity
                        && identity.FindFirst(ClaimTypes.NameIdentifier) is null
                        && identity.FindFirst("sub")?.Value is { Length: > 0 } sub)
                    {
                        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
                    }

                    return Task.CompletedTask;
                },

                // Never surface a raw exception page for a provider round-trip failure
                // (user cancelled, clock skew, misconfig) — send them back to the login page.
                OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/Account/Login?error=external");
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
            };
        });

        var target = settings.MetadataAddress ?? settings.Authority;
        return localAuth.Enabled
            ? $"local accounts + {settings.ProviderLabel} (OIDC) via {target}"
            : $"{settings.ProviderLabel} (OIDC) only via {target}; local accounts disabled";
    }

    /// <summary>
    /// With local sign-in switched off, a cookie belonging to a local account (one issued
    /// before the switch, or a session that predates linking) is no longer honoured — only
    /// principals built for an SSO-managed user carry <see cref="AdditionalUserClaimsPrincipalFactory.ExternalProviderClaimType"/>.
    /// Wraps Identity's security-stamp check rather than replacing it.
    /// </summary>
    private static void RejectLocalSessions(IServiceCollection services) =>
        services.ConfigureApplicationCookie(options =>
        {
            var validateSecurityStamp = options.Events.OnValidatePrincipal;
            options.Events.OnValidatePrincipal = async context =>
            {
                await validateSecurityStamp(context);

                if (context.Principal is { } principal
                    && !principal.HasClaim(c => c.Type == AdditionalUserClaimsPrincipalFactory.ExternalProviderClaimType))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                }
            };
        });
}
