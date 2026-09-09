using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace QaTracker.Web.Hosting.UpdateCheck;

/// <summary>
/// Checks the project's GitHub <c>releases/latest</c> against the running <see cref="BuildInfo"/>.
///
/// Deliberately <b>manual only</b> — there is no background poller. GitHub is contacted solely
/// when someone clicks "Check for updates" on /admin, and even then no more than once per
/// <see cref="MinInterval"/>. The result is held in memory (this is a singleton) and read by
/// the /admin badge and the sidebar dot without any further network calls.
/// </summary>
public sealed class UpdateCheckService
{
    /// <summary>Shortest gap between two real calls to GitHub; extra clicks reuse the last result.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    public const string HttpClientName = "github-releases";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BuildInfo _buildInfo;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly object _gate = new();

    private UpdateStatus? _lastGood;
    private UpdateStatus? _lastOutcome;
    private DateTimeOffset? _lastAttemptAt;

    public UpdateCheckService(
        IHttpClientFactory httpClientFactory,
        BuildInfo buildInfo,
        TimeProvider timeProvider,
        ILogger<UpdateCheckService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _buildInfo = buildInfo;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>The last known status, without contacting GitHub.</summary>
    public UpdateStatus Current
    {
        get
        {
            if (!_buildInfo.UpdateCheckEnabled)
            {
                return UpdateStatus.Disabled;
            }

            lock (_gate)
            {
                return _lastGood ?? _lastOutcome ?? UpdateStatus.NotChecked;
            }
        }
    }

    /// <summary>
    /// Runs a check now (subject to the <see cref="MinInterval"/> throttle) and returns the
    /// resulting status. Never throws — a failed request yields
    /// <see cref="UpdateState.CheckFailed"/> and leaves any earlier good result in place.
    /// </summary>
    public async Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_buildInfo.UpdateCheckEnabled)
        {
            return UpdateStatus.Disabled;
        }

        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (_lastAttemptAt is { } last && now - last < MinInterval)
            {
                return _lastGood ?? _lastOutcome ?? UpdateStatus.NotChecked;
            }

            _lastAttemptAt = now;
        }

        var outcome = await FetchAsync(now, cancellationToken);

        lock (_gate)
        {
            _lastOutcome = outcome;
            if (outcome.State is UpdateState.UpToDate or UpdateState.UpdateAvailable)
            {
                _lastGood = outcome;
            }

            return _lastGood ?? _lastOutcome ?? UpdateStatus.NotChecked;
        }
    }

    private async Task<UpdateStatus> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            using var response = await client.GetAsync(
                $"https://api.github.com/repos/{_buildInfo.GitHubRepo}/releases/latest",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // releases/latest 404s both for "repo has no releases" and "repo doesn't
                // exist" — one more call tells them apart so a mistyped QATRACKER_GITHUB_REPO
                // surfaces as a failure rather than a misleading "up to date".
                using var repoProbe = await client.GetAsync(
                    $"https://api.github.com/repos/{_buildInfo.GitHubRepo}", cancellationToken);

                if (repoProbe.IsSuccessStatusCode)
                {
                    return new UpdateStatus(UpdateState.UpToDate, CheckedAt: now);
                }

                _logger.LogWarning(
                    "Update check: GitHub repo '{Repo}' not reachable ({StatusCode}).",
                    _buildInfo.GitHubRepo, repoProbe.StatusCode);
                return new UpdateStatus(UpdateState.CheckFailed, CheckedAt: now);
            }

            response.EnsureSuccessStatusCode();

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                _logger.LogWarning("GitHub releases/latest returned an unexpected payload.");
                return new UpdateStatus(UpdateState.CheckFailed, CheckedAt: now);
            }

            var updateAvailable = VersionComparison.IsNewer(release.TagName, _buildInfo.Version);

            return new UpdateStatus(
                updateAvailable ? UpdateState.UpdateAvailable : UpdateState.UpToDate,
                LatestVersion: release.TagName.Trim(),
                ReleaseUrl: string.IsNullOrWhiteSpace(release.HtmlUrl) ? _buildInfo.ReleasesUrl : release.HtmlUrl,
                ReleaseName: string.IsNullOrWhiteSpace(release.Name) ? release.TagName.Trim() : release.Name.Trim(),
                CheckedAt: now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Update check against GitHub failed.");
            return new UpdateStatus(UpdateState.CheckFailed, CheckedAt: now);
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
