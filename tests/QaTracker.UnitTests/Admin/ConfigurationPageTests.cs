using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using QaTracker.Web.Admin.Configuration;
using QaTracker.Web.Auth;
using QaTracker.Web.Hosting;
using QaTracker.Web.Hosting.UpdateCheck;
using QaTracker.Web.Storage;
using QaTracker.Web.Telemetry;

namespace QaTracker.UnitTests.Admin;

/// <summary>
/// System settings → Configuration: what each setting may show (never a secret, host or address),
/// that every setting the app reads is listed, the recommendations, and the copyable summary.
/// </summary>
public sealed class ConfigurationPageTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static SettingInfo Setting(string key) => ConfigCatalog.All.Single(s => s.Key == key);

    private static string Show(string key, string? value) =>
        ConfigCatalog.Describe(Setting(key), Config((key, value))).Display;

    // ---- the catalogue --------------------------------------------------------------------------

    [Fact]
    public void Anything_that_looks_like_a_credential_is_a_secret()
    {
        var credential = new Regex("PASSWORD|SECRET|_KEY$|CONNECTION", RegexOptions.IgnoreCase);
        Assert.All(ConfigCatalog.All.Where(s => credential.IsMatch(s.Key)),
            s => Assert.Equal(SettingKind.Secret, s.Kind));
    }

    [Fact]
    public void Every_setting_appears_once()
    {
        var keys = ConfigCatalog.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_QATRACKER_setting_the_app_reads_is_listed()
    {
        // Scan the app's source for setting names, so a new one can't ship without being classified.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "QaTracker.slnx")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        var source = Path.Combine(root.FullName, "src", "QaTracker.Web");
        var used = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains("Migrations"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "\"(QATRACKER_[A-Z0-9_]+)\"").Select(m => m.Groups[1].Value))
            .Where(k => !k.StartsWith("QATRACKER_BUILD_", StringComparison.Ordinal))   // stamped into the image, shown as the version
            .ToHashSet();

        var listed = ConfigCatalog.All.Select(s => s.Key).ToHashSet();
        Assert.Empty(used.Except(listed).Order());
    }

    [Fact]
    public void Each_area_links_to_a_real_section_of_the_configuration_guide()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "QaTracker.slnx")))
        {
            root = root.Parent;
        }
        var anchors = File.ReadAllLines(Path.Combine(root!.FullName, "docs", "configuration.md"))
            .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
            .Select(l => Regex.Replace(l[3..].Trim().ToLowerInvariant(), @"[^\w\- ]", "").Replace(' ', '-'))
            .ToHashSet();
        Assert.All(ConfigCatalog.Areas, a => Assert.Contains(a.DocsAnchor, anchors));
    }

    // ---- what a setting shows ----------------------------------------------------------------

    [Theory]
    [InlineData("QATRACKER_DB_PASSWORD", "hunter2-very-secret")]
    [InlineData("ConnectionStrings:DefaultConnection", "Host=db.internal;Password=hunter2")]
    [InlineData("QATRACKER_DB_HOST", "db.internal.example.net")]
    [InlineData("QATRACKER_S3_ENDPOINT", "http://garage:3900")]
    [InlineData("QATRACKER_S3_BUCKET", "acme-qa-files")]
    [InlineData("QATRACKER_ADMIN_EMAIL", "boss@example.com")]
    public void Secrets_hosts_and_names_only_ever_show_set(string key, string value)
    {
        var view = ConfigCatalog.Describe(Setting(key), Config((key, value)));

        Assert.Equal("Set", view.Display);
        Assert.Equal(SettingState.Set, view.State);
    }

    [Theory]
    [InlineData("QATRACKER_FORWARDED_HEADERS", "true", "On")]
    [InlineData("QATRACKER_FORWARDED_HEADERS", "FALSE", "Off")]
    [InlineData("QATRACKER_FORWARDED_HEADERS", "yes please", "Unrecognised value")]
    [InlineData("QATRACKER_LIVE_POLL_SECONDS", "30", "30")]
    [InlineData("QATRACKER_LIVE_POLL_SECONDS", "thirty", "Unrecognised value")]
    [InlineData("QATRACKER_STORAGE_PROVIDER", "s3", "S3")]
    [InlineData("QATRACKER_STORAGE_PROVIDER", "dropbox", "Unrecognised value")]
    [InlineData("QATRACKER_S3_REGION", "eu-west-2", "eu-west-2")]
    [InlineData("QATRACKER_S3_REGION", "has spaces; and=stuff", "Set")]
    [InlineData("QATRACKER_LIVE_POLL_SECONDS", null, "Default (10)")]
    [InlineData("QATRACKER_OIDC_CLIENT_ID", null, "Not set")]
    public void Plain_values_show_only_once_they_parse(string key, string? value, string expected) =>
        Assert.Equal(expected, Show(key, value));

    // ---- recommendations ---------------------------------------------------------------------

    private static AdvisorInput AllGood() => new(
        StorageProvider.S3, AuthProvider.Entra, LocalAuthEnabled: false, TelemetryProvider.AzureMonitor,
        ForwardedHeadersTrusted: true, RequestCameThroughProxy: true, RequestIsHttps: true, RequestIsLocal: false,
        AdminPasswordSet: false, AdminAccountExists: true, DemoDataOn: false, new UpdateStatus(UpdateState.UpToDate),
        "Production", DetailedErrors: false, []);

    private static IEnumerable<string> Titles(AdvisorInput input) => ConfigAdvisor.For(input).Select(a => a.Title);

    [Fact]
    public void A_complete_set_up_gets_no_recommendations() => Assert.Empty(ConfigAdvisor.For(AllGood()));

    [Fact]
    public void An_untrusted_proxy_is_flagged_first()
    {
        var advice = ConfigAdvisor.For(AllGood() with { ForwardedHeadersTrusted = false, RequestIsHttps = false });

        Assert.Equal(AdviceLevel.Warning, advice[0].Level);
        Assert.Contains("proxy", advice[0].Title);
        Assert.DoesNotContain(advice, a => a.Title.Contains("plain HTTP")); // the proxy explains it
    }

    [Fact]
    public void Plain_http_is_flagged_except_on_localhost()
    {
        var direct = AllGood() with { RequestCameThroughProxy = false, RequestIsHttps = false };

        Assert.Contains(Titles(direct), t => t.Contains("plain HTTP"));
        Assert.DoesNotContain(Titles(direct with { RequestIsLocal = true }), t => t.Contains("plain HTTP"));
    }

    [Fact]
    public void Missing_features_and_tidy_ups_are_suggested()
    {
        var titles = Titles(AllGood() with
        {
            Storage = StorageProvider.None, Auth = AuthProvider.None, LocalAuthEnabled = true, Telemetry = TelemetryProvider.None,
            AdminPasswordSet = true, DemoDataOn = true, Update = new UpdateStatus(UpdateState.UpdateAvailable, "2026.12.01"),
        }).ToList();

        Assert.Contains("Attachments are switched off", titles);
        Assert.Contains("Only local accounts can sign in", titles);
        Assert.Contains("No telemetry", titles);
        Assert.Contains("The first admin's password is still in the configuration", titles);
        Assert.Contains("Demo data is switched on", titles);
        Assert.Contains("Version 2026.12.01 is available", titles);
    }

    [Fact]
    public void Local_passwords_alongside_sso_are_a_suggestion_and_unread_values_a_warning()
    {
        var advice = ConfigAdvisor.For(AllGood() with { LocalAuthEnabled = true, UnrecognisedSettings = ["QATRACKER_LIVE_POLL_SECONDS"] });

        Assert.Contains(advice, a => a.Level == AdviceLevel.Suggestion && a.Title.StartsWith("Local passwords"));
        Assert.Contains(advice, a => a.Level == AdviceLevel.Warning && a.Title.StartsWith("QATRACKER_LIVE_POLL_SECONDS"));
    }

    // ---- the summary and check messages ----------------------------------------------------------

    [Fact]
    public void The_copied_summary_never_contains_a_secret_or_address()
    {
        var config = Config(("QATRACKER_DB_PASSWORD", "hunter2-very-secret"), ("QATRACKER_DB_HOST", "db.internal.example.net"),
            ("QATRACKER_S3_SECRET_KEY", "AKIA-super-secret"), ("QATRACKER_STORAGE_PROVIDER", "S3"),
            ("ConnectionStrings:DefaultConnection", "Host=x;Password=hunter2"));
        var report = new ConfigReport(
            ConfigCatalog.Areas.Select(a => new AreaView(a, a.Settings.Select(s => ConfigCatalog.Describe(s, config)).ToList())).ToList(),
            []);
        var build = new BuildInfo("2026.11.18", null, null, null, null, BuildInfo.DefaultGitHubRepo, UpdateCheckEnabled: true);

        var summary = ConfigReportService.Summary(report, build, [new("Database", CheckOutcome.Ok, "Connected")], DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain("hunter2", summary);
        Assert.DoesNotContain("AKIA", summary);
        Assert.DoesNotContain("db.internal", summary);
        Assert.Contains("QATRACKER_STORAGE_PROVIDER: S3", summary);
        Assert.Contains("QATRACKER_DB_PASSWORD: Set", summary);
        Assert.Contains("Database: Ok — Connected", summary);
    }

    [Theory]
    [InlineData("Connection refused (db.internal:5432)", "Connection refused ([address])")]
    [InlineData("Could not reach http://garage:3900/bucket/key", "Could not reach [address]")]
    [InlineData("Login failed; Host=db.internal;Password=hunter2", "Login failed; [address];[address]")]
    [InlineData("Will retry at '01/01/0001 00:00:00 +00:00'", "Will retry at '01/01/0001 00:00:00 +00:00'")]
    public void Check_errors_hide_addresses_and_credentials(string message, string expected) =>
        Assert.Equal(expected, ConfigChecks.Safe(new InvalidOperationException(message)));
}
