using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;
using QaTracker.Web.Hosting;
using QaTracker.Web.Hosting.UpdateCheck;
using QaTracker.Web.Storage;
using QaTracker.Web.Telemetry;

namespace QaTracker.Web.Admin.Configuration;

/// <summary>One area of the configuration page, with each setting as it looks now.</summary>
public sealed record AreaView(SettingArea Area, IReadOnlyList<SettingView> Settings);

/// <summary>What System settings → Configuration shows (apart from the on-demand checks).</summary>
public sealed record ConfigReport(IReadOnlyList<AreaView> Areas, IReadOnlyList<Advice> Advice);

public sealed class ConfigReportService(
    IConfiguration configuration,
    IHostEnvironment environment,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UpdateCheckService updateCheck,
    BuildInfo build)
{
    public async Task<ConfigReport> BuildAsync(HttpRequest request, CancellationToken ct = default)
    {
        var areas = ConfigCatalog.Areas
            .Select(a => new AreaView(a, a.Settings.Select(s => ConfigCatalog.Describe(s, configuration)).ToList()))
            .ToList();

        var authProvider = AuthConfig.ResolveProvider(configuration);
        var adminEmail = configuration["QATRACKER_ADMIN_EMAIL"]?.Trim();
        var adminExists = false;
        if (!string.IsNullOrEmpty(adminEmail))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var normalized = adminEmail.ToUpperInvariant();
            adminExists = await db.Users.AnyAsync(u => u.NormalizedEmail == normalized && u.DeletedUtc == null, ct);
        }

        var input = new AdvisorInput(
            Storage: StorageOptions.ResolveProvider(configuration),
            Auth: authProvider,
            LocalAuthEnabled: AuthConfig.ResolveLocalAuth(configuration, authProvider).Enabled,
            Telemetry: TelemetryOptions.ResolveProvider(configuration),
            ForwardedHeadersTrusted: bool.TryParse(configuration["QATRACKER_FORWARDED_HEADERS"], out var fwd) && fwd,
            RequestCameThroughProxy: CameThroughProxy(request),
            RequestIsHttps: request.IsHttps,
            RequestIsLocal: IsLocal(request),
            AdminPasswordSet: !string.IsNullOrWhiteSpace(configuration["QATRACKER_ADMIN_PASSWORD"]),
            AdminAccountExists: adminExists,
            DemoDataOn: Demo.DemoData.Enabled(configuration),
            Update: updateCheck.Current,
            Environment: environment.EnvironmentName,
            DetailedErrors: bool.TryParse(configuration["QATRACKER_DETAILED_ERRORS"], out var detailed) && detailed,
            UnrecognisedSettings: areas.SelectMany(a => a.Settings).Where(s => s.State == SettingState.Unrecognised)
                .Select(s => s.Info.Key).ToList());

        return new ConfigReport(areas, ConfigAdvisor.For(input));
    }

    /// <summary>A docs page on GitHub, e.g. "configuration.md#telemetry" (for the repo this build reports to).</summary>
    public string DocsUrl(string doc) => $"https://github.com/{build.GitHubRepo}/blob/master/docs/{doc}";

    /// <summary>Proxy headers on the request — still there when untrusted, moved to X-Original-* once the
    /// forwarded-headers middleware has applied them.</summary>
    public static bool CameThroughProxy(HttpRequest request) =>
        new[] { "X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host", "Forwarded", "X-Original-For", "X-Original-Proto" }
            .Any(h => request.Headers.ContainsKey(h));

    public static bool IsLocal(HttpRequest request) =>
        request.Host.Host is "localhost" or "127.0.0.1" or "::1" or "[::1]"
        || request.Host.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A plain-text summary for a support request or GitHub issue: the version, every setting as the
    /// page shows it (so no secret, host or address), the last checks and the recommendations.
    /// </summary>
    public static string Summary(ConfigReport report, BuildInfo build, IReadOnlyList<CheckResult>? checks, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine("QA Tracker configuration summary");
        text.AppendLine($"Version: {build.Version}{(build.SourceRef is { } source ? $" ({source})" : "")}");
        text.AppendLine($"Generated: {now.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC");
        text.AppendLine("Secrets, hosts and addresses show only whether they're set.");

        foreach (var area in report.Areas)
        {
            text.AppendLine();
            text.AppendLine($"[{area.Area.Title}]");
            foreach (var setting in area.Settings)
            {
                text.AppendLine($"  {setting.Info.Key}: {setting.Display}");
            }
        }

        text.AppendLine();
        text.AppendLine("[Checks]");
        if (checks is null)
        {
            text.AppendLine("  Not run");
        }
        else
        {
            foreach (var check in checks)
            {
                text.AppendLine($"  {check.Name}: {check.Outcome} — {check.Detail}");
            }
        }

        text.AppendLine();
        text.AppendLine("[Recommendations]");
        if (report.Advice.Count == 0)
        {
            text.AppendLine("  None");
        }
        foreach (var advice in report.Advice)
        {
            text.AppendLine($"  {(advice.Level == AdviceLevel.Warning ? "!" : "-")} {advice.Title}");
        }

        return text.ToString().TrimEnd();
    }
}
