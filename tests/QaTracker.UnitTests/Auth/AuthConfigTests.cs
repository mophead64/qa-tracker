using Microsoft.Extensions.Configuration;
using QaTracker.Web.Auth;

namespace QaTracker.UnitTests.Auth;

public class AuthConfigTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData("Keycloak", AuthProvider.Keycloak)]
    [InlineData("keycloak", AuthProvider.Keycloak)]
    [InlineData("Entra", AuthProvider.Entra)]
    [InlineData("EntraId", AuthProvider.Entra)]
    [InlineData("AzureAd", AuthProvider.Entra)]
    [InlineData("AAD", AuthProvider.Entra)]
    [InlineData("", AuthProvider.None)]
    [InlineData("nonsense", AuthProvider.None)]
    public void Resolves_provider_from_env_var(string value, AuthProvider expected)
    {
        Assert.Equal(expected, AuthConfig.ResolveProvider(Config(new() { ["QATRACKER_AUTH_PROVIDER"] = value })));
    }

    [Fact]
    public void Provider_is_none_when_unset()
    {
        Assert.Equal(AuthProvider.None, AuthConfig.ResolveProvider(Config(new())));
    }

    private static Dictionary<string, string?> MinimalOidc() => new()
    {
        ["QATRACKER_OIDC_AUTHORITY"] = "http://localhost:8081/realms/qatracker/",
        ["QATRACKER_OIDC_CLIENT_ID"] = "qatracker-web",
        ["QATRACKER_OIDC_CLIENT_SECRET"] = "s3cret",
    };

    [Fact]
    public void Resolves_oidc_settings_with_defaults()
    {
        var settings = AuthConfig.ResolveOidc(Config(MinimalOidc()), AuthProvider.Keycloak);

        Assert.Equal("Keycloak", settings.ProviderLabel);
        Assert.Equal("http://localhost:8081/realms/qatracker", settings.Authority); // trailing slash trimmed
        Assert.Null(settings.MetadataAddress);
        Assert.Equal("qatracker-web", settings.ClientId);
        Assert.Equal("s3cret", settings.ClientSecret);
        Assert.Equal(["openid", "profile", "email"], settings.Scopes);
        Assert.True(settings.RequireHttpsMetadata);
        Assert.Equal("roles", settings.RoleClaimType);
        Assert.Equal("QA", settings.QaRoleValue);
        Assert.Equal("Dev", settings.DevRoleValue);
    }

    [Fact]
    public void Entra_provider_label()
    {
        Assert.Equal("Microsoft Entra ID", AuthConfig.ResolveOidc(Config(MinimalOidc()), AuthProvider.Entra).ProviderLabel);
    }

    [Theory]
    [InlineData("QATRACKER_OIDC_AUTHORITY")]
    [InlineData("QATRACKER_OIDC_CLIENT_ID")]
    [InlineData("QATRACKER_OIDC_CLIENT_SECRET")]
    public void Required_oidc_vars_throw_when_missing(string missingKey)
    {
        var values = MinimalOidc();
        values.Remove(missingKey);

        Assert.Throws<InvalidOperationException>(() => AuthConfig.ResolveOidc(Config(values), AuthProvider.Keycloak));
    }

    [Fact]
    public void Scopes_are_parsed_and_openid_is_forced_in()
    {
        var values = MinimalOidc();
        values["QATRACKER_OIDC_SCOPES"] = "profile,email offline_access";

        var settings = AuthConfig.ResolveOidc(Config(values), AuthProvider.Keycloak);

        Assert.Equal("openid", settings.Scopes[0]);
        Assert.Contains("offline_access", settings.Scopes);
    }

    [Fact]
    public void Metadata_address_and_flags_pass_through()
    {
        var values = MinimalOidc();
        values["QATRACKER_OIDC_METADATA_ADDRESS"] = "http://keycloak:8080/realms/qatracker/.well-known/openid-configuration";
        values["QATRACKER_OIDC_REQUIRE_HTTPS_METADATA"] = "false";
        values["QATRACKER_OIDC_ROLE_CLAIM"] = "groups";
        values["QATRACKER_OIDC_ROLE_QA_VALUE"] = "qa-team";
        values["QATRACKER_OIDC_ROLE_DEV_VALUE"] = "dev-team";

        var settings = AuthConfig.ResolveOidc(Config(values), AuthProvider.Keycloak);

        Assert.Equal("http://keycloak:8080/realms/qatracker/.well-known/openid-configuration", settings.MetadataAddress);
        Assert.False(settings.RequireHttpsMetadata);
        Assert.Equal("groups", settings.RoleClaimType);
        Assert.Equal("qa-team", settings.QaRoleValue);
        Assert.Equal("dev-team", settings.DevRoleValue);
    }
}
