using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using QaTracker.Web.Auth;

namespace QaTracker.UnitTests.Auth;

public class OidcCookieTests
{
    private static OpenIdConnectOptions BuildOidcOptions(
        Dictionary<string, string?> config,
        string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(config);
        // The Development container validates the whole DI graph on Build(); this test only
        // wires up authentication, so skip that.
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = false;
            options.ValidateScopes = false;
        });
        builder.AddAppAuthentication();

        using var app = builder.Build();
        return app.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(AuthConfig.OidcScheme);
    }

    private static Dictionary<string, string?> EntraConfig() => new()
    {
        ["QATRACKER_AUTH_PROVIDER"] = "Entra",
        ["QATRACKER_OIDC_AUTHORITY"] = "https://login.microsoftonline.com/tenant/v2.0",
        ["QATRACKER_OIDC_CLIENT_ID"] = "qatracker-web",
        ["QATRACKER_OIDC_CLIENT_SECRET"] = "s3cret",
    };

    private static void AssertRelaxedForPlainHttp(OpenIdConnectOptions options)
    {
        Assert.Equal(SameSiteMode.Lax, options.CorrelationCookie.SameSite);
        Assert.Equal(SameSiteMode.Lax, options.NonceCookie.SameSite);
        // ...and not Secure, so a plain-HTTP browser keeps them at all (the framework
        // default marks both Secure, which http drops).
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.CorrelationCookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.NonceCookie.SecurePolicy);
        // Query response mode -> the callback is a GET, which carries the Lax cookie even
        // though the IdP responds from a different origin.
        Assert.Equal(OpenIdConnectResponseMode.Query, options.ResponseMode);
    }

    [Fact]
    public void Http_authority_relaxes_the_correlation_and_nonce_cookies()
    {
        // A plain-HTTP IdP (bundled Keycloak) can't set Secure cookies regardless of how
        // this app is hosted.
        var options = BuildOidcOptions(new()
        {
            ["QATRACKER_AUTH_PROVIDER"] = "Keycloak",
            ["QATRACKER_OIDC_AUTHORITY"] = "http://localhost:8081/realms/qatracker",
            ["QATRACKER_OIDC_CLIENT_ID"] = "qatracker-web",
            ["QATRACKER_OIDC_CLIENT_SECRET"] = "s3cret",
            ["QATRACKER_OIDC_REQUIRE_HTTPS_METADATA"] = "false",
        });

        AssertRelaxedForPlainHttp(options);
    }

    [Fact]
    public void Https_authority_in_development_without_tls_also_relaxes_the_cookies()
    {
        // Real Entra tenant, but the app is served over plain HTTP in dev (no HTTPS
        // redirect, no forwarded headers) — the callback still can't see a Secure cookie.
        var options = BuildOidcOptions(EntraConfig(), environment: "Development");

        AssertRelaxedForPlainHttp(options);
    }

    [Fact]
    public void Https_authority_in_production_keeps_the_hardened_defaults()
    {
        var options = BuildOidcOptions(EntraConfig(), environment: "Production");

        Assert.Equal(SameSiteMode.None, options.CorrelationCookie.SameSite);
        Assert.Equal(SameSiteMode.None, options.NonceCookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, options.CorrelationCookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, options.NonceCookie.SecurePolicy);
        Assert.Equal(OpenIdConnectResponseMode.FormPost, options.ResponseMode);
    }

    [Fact]
    public void Https_authority_in_development_behind_a_tls_proxy_keeps_the_hardened_defaults()
    {
        var config = EntraConfig();
        config["QATRACKER_FORWARDED_HEADERS"] = "true";

        var options = BuildOidcOptions(config, environment: "Development");

        Assert.Equal(SameSiteMode.None, options.CorrelationCookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, options.CorrelationCookie.SecurePolicy);
        Assert.Equal(OpenIdConnectResponseMode.FormPost, options.ResponseMode);
    }
}
