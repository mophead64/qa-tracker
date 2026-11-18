using System.Globalization;
using System.Text.RegularExpressions;

namespace QaTracker.Web.Admin.Configuration;

/// <summary>
/// How a setting may be shown on System settings → Configuration. The page only ever lists the
/// settings in <see cref="ConfigCatalog"/> (it never walks the environment), and each one's kind
/// decides what of its value can appear:
/// <list type="bullet">
///   <item><see cref="Switch"/>, <see cref="Number"/>, <see cref="Choice"/>: the value, but only once
///     it parses as one ("Unrecognised value" otherwise, never the raw text).</item>
///   <item><see cref="Name"/>: a short identifier such as a region or service name, shown only when it
///     looks like one.</item>
///   <item><see cref="Presence"/> (hosts, URLs, user names, bucket names…) and <see cref="Secret"/>
///     (passwords, keys, connection strings): only "Set" / "Not set".</item>
/// </list>
/// </summary>
public enum SettingKind
{
    Switch,
    Number,
    Choice,
    Name,
    Presence,
    Secret,
}

/// <summary>One setting: its variable, what it's for, how it may be shown, and its default in words.</summary>
public sealed record SettingInfo(
    string Key, string Purpose, SettingKind Kind, string? Default = null, IReadOnlyList<string>? Choices = null);

/// <summary>An area of settings, matching a section of docs/configuration.md.</summary>
public sealed record SettingArea(string Title, string DocsAnchor, IReadOnlyList<SettingInfo> Settings);

/// <summary>How one setting looks right now. <see cref="Display"/> is always safe to show (see <see cref="SettingKind"/>).</summary>
public sealed record SettingView(SettingInfo Info, SettingState State, string Display);

public enum SettingState
{
    /// <summary>Not set: the default applies.</summary>
    Default,

    /// <summary>Set to a recognised value.</summary>
    Set,

    /// <summary>Set, but not to anything the app understands (it falls back to the default).</summary>
    Unrecognised,
}

public static partial class ConfigCatalog
{
    private static SettingInfo S(string key, string purpose, string? dflt = null) => new(key, purpose, SettingKind.Switch, dflt);
    private static SettingInfo N(string key, string purpose, string? dflt = null) => new(key, purpose, SettingKind.Number, dflt);
    private static SettingInfo C(string key, string purpose, string? dflt, params string[] choices) => new(key, purpose, SettingKind.Choice, dflt, choices);
    private static SettingInfo Id(string key, string purpose, string? dflt = null) => new(key, purpose, SettingKind.Name, dflt);
    private static SettingInfo P(string key, string purpose, string? dflt = null) => new(key, purpose, SettingKind.Presence, dflt);
    private static SettingInfo X(string key, string purpose) => new(key, purpose, SettingKind.Secret);

    /// <summary>Every setting QA Tracker reads, by area, in the order of docs/configuration.md.</summary>
    public static readonly IReadOnlyList<SettingArea> Areas =
    [
        new("Database", "database",
        [
            X("ConnectionStrings:DefaultConnection", "A full connection string, used instead of the settings below"),
            P("QATRACKER_DB_HOST", "Database host", "localhost"),
            N("QATRACKER_DB_PORT", "Database port", "5432"),
            P("QATRACKER_DB_NAME", "Database name", "qatracker"),
            P("QATRACKER_DB_USER", "Database user", "qatracker"),
            X("QATRACKER_DB_PASSWORD", "Database password"),
            N("QATRACKER_DB_MAX_POOL_SIZE", "Most connections per instance", "20"),
            N("QATRACKER_DB_MIN_POOL_SIZE", "Fewest connections kept open per instance", "1"),
            S("QATRACKER_MIGRATE_ON_STARTUP", "Apply database changes on start-up", "On"),
        ]),
        new("First admin account", "first-admin-account",
        [
            P("QATRACKER_ADMIN_EMAIL", "The first QA account, created on first start-up"),
            X("QATRACKER_ADMIN_PASSWORD", "That account's password"),
        ]),
        new("Demo data", "demo-data",
        [
            S("QATRACKER_DEMO_DATA", "Fill an empty install with example projects", "Off"),
        ]),
        new("Sign-in", "sign-in",
        [
            C("QATRACKER_AUTH_PROVIDER", "Single sign-on provider", "None (local accounts only)", "Entra", "Keycloak"),
            S("QATRACKER_LOCAL_AUTH_ENABLED", "Local accounts alongside single sign-on", "On"),
            P("QATRACKER_OIDC_CLIENT_ID", "App registration / client ID"),
            X("QATRACKER_OIDC_CLIENT_SECRET", "Client secret"),
            P("QATRACKER_OIDC_TENANT_ID", "Entra directory (tenant) ID"),
            P("QATRACKER_OIDC_AUTHORITY", "Provider's issuer URL"),
            P("QATRACKER_OIDC_INSTANCE", "Entra national cloud", "login.microsoftonline.com"),
            P("QATRACKER_OIDC_METADATA_ADDRESS", "Discovery URL for the app's own calls"),
            P("QATRACKER_OIDC_SCOPES", "Scopes requested", "openid profile email"),
            Id("QATRACKER_OIDC_ROLE_CLAIM", "Claim carrying the role", "roles"),
            Id("QATRACKER_OIDC_ROLE_QA_VALUE", "Claim value meaning QA", "QA"),
            Id("QATRACKER_OIDC_ROLE_DEV_VALUE", "Claim value meaning Dev", "Dev"),
            Id("QATRACKER_OIDC_ROLE_VIEWER_VALUE", "Claim value meaning Viewer", "Viewer"),
            S("QATRACKER_OIDC_REQUIRE_HTTPS_METADATA", "Require HTTPS for the provider's metadata", "On"),
        ]),
        new("Attachment storage", "attachment-storage",
        [
            C("QATRACKER_STORAGE_PROVIDER", "Where attachments are kept", "None (uploads off)", "S3", "Azure"),
            P("QATRACKER_S3_BUCKET", "S3 bucket"),
            Id("QATRACKER_S3_REGION", "S3 region", "us-east-1"),
            X("QATRACKER_S3_ACCESS_KEY", "S3 access key"),
            X("QATRACKER_S3_SECRET_KEY", "S3 secret key"),
            P("QATRACKER_S3_ENDPOINT", "S3-compatible store's address (Garage, MinIO…)"),
            S("QATRACKER_S3_FORCE_PATH_STYLE", "Path-style S3 addresses", "On with an endpoint"),
            X("QATRACKER_AZURE_STORAGE_CONNECTION_STRING", "Azure storage connection string"),
            P("QATRACKER_AZURE_CONTAINER", "Azure blob container", "qatracker-attachments"),
            N("QATRACKER_MAX_UPLOAD_MB", "Largest file the server accepts (MB)", "20"),
        ]),
        new("Behind a proxy and scaling out", "behind-a-proxy-and-scaling-out",
        [
            S("QATRACKER_FORWARDED_HEADERS", "Trust X-Forwarded-* headers from a proxy", "Off"),
            P("QATRACKER_KNOWN_PROXIES", "Proxies to trust"),
            P("QATRACKER_KNOWN_NETWORKS", "Networks to trust"),
            N("QATRACKER_FORWARDED_HEADERS_LIMIT", "Proxy hops to unwrap", "No limit"),
            S("QATRACKER_HTTPS_REDIRECT", "Redirect HTTP to HTTPS in the app", "Off"),
        ]),
        new("Telemetry", "telemetry",
        [
            C("QATRACKER_TELEMETRY_PROVIDER", "Where traces, metrics and logs go", "None (off)", "Otlp", "AzureMonitor"),
            P("QATRACKER_OTLP_ENDPOINT", "OpenTelemetry collector's address"),
            C("QATRACKER_OTLP_PROTOCOL", "OTLP protocol", "grpc", "grpc", "http/protobuf"),
            X("QATRACKER_AZURE_MONITOR_CONNECTION_STRING", "Application Insights connection string"),
            Id("QATRACKER_TELEMETRY_SERVICE_NAME", "Service name reported", "qa-tracker"),
            C("QATRACKER_TELEMETRY_LOG_LEVEL", "Lowest log level exported", "Warning",
                "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"),
        ]),
        new("Notifications and live pages", "notifications-and-live-pages",
        [
            N("QATRACKER_NOTIFICATIONS_POLL_SECONDS", "Seconds between notification checks", "45"),
            N("QATRACKER_LIVE_POLL_SECONDS", "Seconds between live-page checks", "10"),
        ]),
        new("Version and update check", "version-and-update-check",
        [
            S("QATRACKER_UPDATE_CHECK", "Check GitHub for newer releases", "On"),
            Id("QATRACKER_GITHUB_REPO", "Repository checked for releases", "mophead64/qa-tracker"),
        ]),
        new("Runtime and errors", "runtime-and-errors",
        [
            Id("ASPNETCORE_ENVIRONMENT", "Runtime environment", "Production"),
            P("ASPNETCORE_HTTP_PORTS", "Port(s) the app listens on", "8080 in the image"),
            S("QATRACKER_DETAILED_ERRORS", "Detailed developer error pages", "Off"),
        ]),
    ];

    public static IEnumerable<SettingInfo> All => Areas.SelectMany(a => a.Settings);

    // A short identifier: letters, digits and . _ - / only.
    [GeneratedRegex(@"^[A-Za-z0-9._/-]{1,64}$")]
    private static partial Regex NameShape();

    /// <summary>How <paramref name="setting"/> looks under <paramref name="configuration"/> — never more than its kind allows.</summary>
    public static SettingView Describe(SettingInfo setting, IConfiguration configuration)
    {
        var raw = configuration[setting.Key]?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return new SettingView(setting, SettingState.Default, setting.Default is { } d ? $"Default ({d})" : "Not set");
        }

        return setting.Kind switch
        {
            SettingKind.Secret or SettingKind.Presence => new(setting, SettingState.Set, "Set"),
            SettingKind.Switch => bool.TryParse(raw, out var on)
                ? new(setting, SettingState.Set, on ? "On" : "Off")
                : Unrecognised(setting),
            SettingKind.Number => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? new(setting, SettingState.Set, n.ToString(CultureInfo.InvariantCulture))
                : Unrecognised(setting),
            SettingKind.Choice => setting.Choices?.FirstOrDefault(c => c.Equals(raw, StringComparison.OrdinalIgnoreCase)) is { } choice
                ? new(setting, SettingState.Set, choice)
                : Unrecognised(setting),
            SettingKind.Name => NameShape().IsMatch(raw)
                ? new(setting, SettingState.Set, raw)
                : new(setting, SettingState.Set, "Set"),
            _ => new(setting, SettingState.Set, "Set"),
        };
    }

    private static SettingView Unrecognised(SettingInfo setting) => new(setting, SettingState.Unrecognised, "Unrecognised value");
}
