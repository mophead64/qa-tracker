namespace QaTracker.Web.Hosting;

/// <summary>
/// Identity of the running build, shown in the "System status" panel on /admin and used by
/// <see cref="UpdateCheck.UpdateCheckService"/> to compare against the latest GitHub release.
///
/// The values come from <c>QATRACKER_BUILD_*</c> environment variables that the Dockerfile's
/// final stage stamps in at image-build time (fed by CI — see <c>.github/workflows/ci.yml</c>).
/// Running from source (<c>dotnet run</c>) they're absent, so <see cref="Version"/> is
/// <c>"Development"</c>.
/// </summary>
public sealed record BuildInfo(
    string Version,
    string? Branch,
    string? Commit,
    string? CommitShort,
    DateTimeOffset? BuildDate,
    string GitHubRepo,
    bool UpdateCheckEnabled)
{
    public const string DefaultGitHubRepo = "mophead64/qa-tracker";

    private const string DevelopmentVersion = "Development";

    public bool IsDevelopment => Version == DevelopmentVersion;

    /// <summary>Link to the exact commit this build was produced from, or null when unknown.</summary>
    public string? CommitUrl =>
        string.IsNullOrEmpty(Commit) ? null : $"https://github.com/{GitHubRepo}/commit/{Commit}";

    /// <summary>Base URL of the repo's releases page.</summary>
    public string ReleasesUrl => $"https://github.com/{GitHubRepo}/releases";

    /// <summary><c>branch:shortsha</c> when both are known, otherwise null.</summary>
    public string? SourceRef =>
        !string.IsNullOrEmpty(Branch) && !string.IsNullOrEmpty(CommitShort)
            ? $"{Branch}:{CommitShort}"
            : null;

    public static BuildInfo FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var stampedVersion = Trimmed(configuration["QATRACKER_BUILD_VERSION"]);

        var version = stampedVersion
            ?? (environment.IsDevelopment() ? DevelopmentVersion : "unknown");

        var repo = Trimmed(configuration["QATRACKER_GITHUB_REPO"]) ?? DefaultGitHubRepo;

        // On by default, but never in Development (nothing sane to compare) and opt-out-able
        // for air-gapped deployments via QATRACKER_UPDATE_CHECK=false.
        var updateCheckEnabled =
            version != DevelopmentVersion
            && configuration.GetValue("QATRACKER_UPDATE_CHECK", true);

        return new BuildInfo(
            Version: version,
            Branch: Trimmed(configuration["QATRACKER_BUILD_BRANCH"]),
            Commit: Trimmed(configuration["QATRACKER_BUILD_COMMIT"]),
            CommitShort: Trimmed(configuration["QATRACKER_BUILD_COMMIT_SHORT"]),
            BuildDate: DateTimeOffset.TryParse(configuration["QATRACKER_BUILD_DATE"], out var built)
                ? built
                : null,
            GitHubRepo: repo,
            UpdateCheckEnabled: updateCheckEnabled);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
