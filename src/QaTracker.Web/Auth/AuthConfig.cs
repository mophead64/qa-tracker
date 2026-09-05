namespace QaTracker.Web.Auth;

/// <summary>
/// Which sign-in mechanism is configured. Local accounts always work; at most one external
/// OpenID Connect provider runs alongside them — never two.
/// </summary>
public enum AuthProvider
{
    None,
    Entra,
    Keycloak,
}

/// <summary>
/// Resolved OpenID Connect settings. A single generic OIDC handler covers both Entra and
/// Keycloak — the provider only changes the display label and a few claim defaults.
/// </summary>
public sealed record OidcAuthSettings(
    string ProviderLabel,
    string Authority,
    string? MetadataAddress,
    string ClientId,
    string ClientSecret,
    IReadOnlyList<string> Scopes,
    bool RequireHttpsMetadata,
    string RoleClaimType,
    string QaRoleValue,
    string DevRoleValue);

/// <summary>
/// Resolves the external auth provider from <c>QATRACKER_AUTH_PROVIDER</c> plus its
/// <c>QATRACKER_OIDC_*</c> env vars, mirroring the precedence style of
/// <see cref="Storage.StorageOptions"/> and <see cref="Telemetry.TelemetryOptions"/>.
/// </summary>
public static class AuthConfig
{
    /// <summary>The authentication scheme name for the external OIDC handler.</summary>
    public const string OidcScheme = "oidc";

    public static AuthProvider ResolveProvider(IConfiguration configuration) =>
        configuration["QATRACKER_AUTH_PROVIDER"]?.Trim().ToUpperInvariant() switch
        {
            "ENTRA" or "ENTRAID" or "AZUREAD" or "AAD" => AuthProvider.Entra,
            "KEYCLOAK" => AuthProvider.Keycloak,
            _ => AuthProvider.None,
        };

    public static OidcAuthSettings ResolveOidc(IConfiguration configuration, AuthProvider provider)
    {
        var metadata = configuration["QATRACKER_OIDC_METADATA_ADDRESS"];
        var scopes = configuration["QATRACKER_OIDC_SCOPES"];

        return new OidcAuthSettings(
            ProviderLabel: provider switch
            {
                AuthProvider.Entra => "Microsoft Entra ID",
                AuthProvider.Keycloak => "Keycloak",
                _ => "SSO",
            },
            Authority: Require(configuration, "QATRACKER_OIDC_AUTHORITY").TrimEnd('/'),
            MetadataAddress: string.IsNullOrWhiteSpace(metadata) ? null : metadata,
            ClientId: Require(configuration, "QATRACKER_OIDC_CLIENT_ID"),
            ClientSecret: Require(configuration, "QATRACKER_OIDC_CLIENT_SECRET"),
            Scopes: ParseScopes(scopes),
            RequireHttpsMetadata: Bool(configuration, "QATRACKER_OIDC_REQUIRE_HTTPS_METADATA", defaultValue: true),
            RoleClaimType: configuration["QATRACKER_OIDC_ROLE_CLAIM"] is { Length: > 0 } claim ? claim : "roles",
            QaRoleValue: configuration["QATRACKER_OIDC_ROLE_QA_VALUE"] is { Length: > 0 } qa ? qa : "QA",
            DevRoleValue: configuration["QATRACKER_OIDC_ROLE_DEV_VALUE"] is { Length: > 0 } dev ? dev : "Dev");
    }

    private static IReadOnlyList<string> ParseScopes(string? value)
    {
        var scopes = (value ?? "openid profile email")
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // openid is mandatory for an OIDC flow; add it back if the operator dropped it.
        if (!scopes.Contains("openid", StringComparer.OrdinalIgnoreCase))
        {
            scopes.Insert(0, "openid");
        }

        return scopes;
    }

    private static bool Bool(IConfiguration configuration, string key, bool defaultValue) =>
        bool.TryParse(configuration[key], out var parsed) ? parsed : defaultValue;

    private static string Require(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required when QATRACKER_AUTH_PROVIDER is set.");
}
