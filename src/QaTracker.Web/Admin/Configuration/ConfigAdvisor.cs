using QaTracker.Web.Auth;
using QaTracker.Web.Hosting.UpdateCheck;
using QaTracker.Web.Storage;
using QaTracker.Web.Telemetry;

namespace QaTracker.Web.Admin.Configuration;

public enum AdviceLevel
{
    /// <summary>Probably wrong, or risky: worth fixing.</summary>
    Warning,

    /// <summary>Something they could switch on, or tidy up.</summary>
    Suggestion,
}

/// <summary>One recommendation. <paramref name="Doc"/> is a docs path with anchor, e.g. "configuration.md#telemetry".</summary>
public sealed record Advice(AdviceLevel Level, string Title, string Detail, string? Doc = null, string? Link = null);

/// <summary>Everything the recommendations look at: how the install is set up, and the request the page was viewed through.</summary>
public sealed record AdvisorInput(
    StorageProvider Storage,
    AuthProvider Auth,
    bool LocalAuthEnabled,
    TelemetryProvider Telemetry,
    bool ForwardedHeadersTrusted,
    bool RequestCameThroughProxy,
    bool RequestIsHttps,
    bool RequestIsLocal,
    bool AdminPasswordSet,
    bool AdminAccountExists,
    bool DemoDataOn,
    UpdateStatus Update,
    string Environment,
    bool DetailedErrors,
    IReadOnlyList<string> UnrecognisedSettings);

/// <summary>
/// The "what you're missing out on" list on System settings → Configuration: likely mistakes first
/// (a proxy whose headers aren't trusted, plain HTTP, a value the app can't read…), then features
/// that are off and tidy-ups. Pure rules over <see cref="AdvisorInput"/>, so each is unit-tested.
/// </summary>
public static class ConfigAdvisor
{
    public static IReadOnlyList<Advice> For(AdvisorInput x)
    {
        var advice = new List<Advice>();

        // ---- likely mistakes -----------------------------------------------------------------
        if (x.RequestCameThroughProxy && !x.ForwardedHeadersTrusted)
        {
            advice.Add(new(AdviceLevel.Warning, "This page came through a proxy whose headers aren't trusted",
                "The app sees plain http:// and the proxy's address instead of the visitor's, so redirects and the single " +
                "sign-on callback can break. Set QATRACKER_FORWARDED_HEADERS=true.",
                "configuration.md#behind-a-proxy-and-scaling-out"));
        }
        else if (!x.RequestIsHttps && !x.RequestIsLocal)
        {
            advice.Add(new(AdviceLevel.Warning, "QA Tracker is being served over plain HTTP",
                "Passwords and sign-in cookies cross the network unencrypted. Put TLS in front of the app (a reverse proxy or " +
                "your platform's ingress).", "deployment.md#behind-a-reverse-proxy"));
        }

        foreach (var key in x.UnrecognisedSettings)
        {
            advice.Add(new(AdviceLevel.Warning, $"{key} has a value QA Tracker doesn't recognise",
                "It's being ignored, so the default applies. Check the accepted values in the configuration guide.",
                "configuration.md"));
        }

        if (x.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase) && !x.RequestIsLocal)
        {
            advice.Add(new(AdviceLevel.Warning, "Running in Development mode",
                "ASPNETCORE_ENVIRONMENT=Development turns on developer tooling. Use Production for anything others can reach.",
                "configuration.md#runtime-and-errors"));
        }

        if (x.DetailedErrors)
        {
            advice.Add(new(AdviceLevel.Warning, "Detailed error pages are switched on",
                "QATRACKER_DETAILED_ERRORS shows internals when something fails. Turn it off outside development.",
                "configuration.md#runtime-and-errors"));
        }

        // ---- things that are off ---------------------------------------------------------------
        if (x.Storage == StorageProvider.None)
        {
            advice.Add(new(AdviceLevel.Suggestion, "Attachments are switched off",
                "Connect Amazon S3, Azure Blob Storage or an S3-compatible store (Garage, MinIO) so people can attach " +
                "screenshots, recordings and logs to projects, test cases, defects and comments.",
                "configuration.md#attachment-storage"));
        }

        if (x.Auth == AuthProvider.None)
        {
            advice.Add(new(AdviceLevel.Suggestion, "Only local accounts can sign in",
                "Connect Microsoft Entra ID or Keycloak so people sign in with their company account, get their role from it, " +
                "and their QA Tracker account is created for them.", "sso.md"));
        }
        else if (x.LocalAuthEnabled)
        {
            advice.Add(new(AdviceLevel.Suggestion, "Local passwords still work alongside single sign-on",
                "Once everyone signs in with single sign-on, set QATRACKER_LOCAL_AUTH_ENABLED=false so it's the only way in.",
                "configuration.md#sign-in"));
        }

        if (x.Telemetry == TelemetryProvider.None)
        {
            advice.Add(new(AdviceLevel.Suggestion, "No telemetry",
                "Send traces, metrics and logs to Application Insights or any OpenTelemetry backend to see slow pages and " +
                "errors as they happen.", "configuration.md#telemetry"));
        }

        // ---- tidy-ups --------------------------------------------------------------------------
        if (x.AdminPasswordSet && x.AdminAccountExists)
        {
            advice.Add(new(AdviceLevel.Suggestion, "The first admin's password is still in the configuration",
                "QATRACKER_ADMIN_PASSWORD is only used to create that account, which now exists. Remove it from the " +
                "environment (and change the account's password under Settings if it was ever shared).",
                "configuration.md#first-admin-account"));
        }

        if (x.DemoDataOn)
        {
            advice.Add(new(AdviceLevel.Suggestion, "Demo data is switched on",
                "It only ever fills an empty database, so it's harmless now, but turn QATRACKER_DEMO_DATA off for a real install.",
                "configuration.md#demo-data"));
        }

        if (x.Update.State == UpdateState.UpdateAvailable)
        {
            advice.Add(new(AdviceLevel.Suggestion, $"Version {x.Update.LatestVersion} is available",
                "See what's new in the release notes, then pull the new image and restart.",
                "deployment.md#upgrading", x.Update.ReleaseUrl));
        }

        return advice;
    }
}
