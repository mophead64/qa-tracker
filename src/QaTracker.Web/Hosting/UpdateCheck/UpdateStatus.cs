namespace QaTracker.Web.Hosting.UpdateCheck;

public enum UpdateState
{
    /// <summary>Update checking is switched off (Development, or QATRACKER_UPDATE_CHECK=false).</summary>
    Disabled,

    /// <summary>Enabled, but nobody has run a check yet since the app started.</summary>
    NotChecked,

    /// <summary>The running build is the latest release (or there are no releases to compare against).</summary>
    UpToDate,

    /// <summary>A newer release exists on GitHub.</summary>
    UpdateAvailable,

    /// <summary>The last check couldn't reach GitHub / got an error response.</summary>
    CheckFailed,
}

/// <summary>Result of the most recent (manual) update check.</summary>
/// <param name="State">Outcome.</param>
/// <param name="LatestVersion">Tag of the latest release, when known.</param>
/// <param name="ReleaseUrl">Link to that release on GitHub, when known.</param>
/// <param name="ReleaseName">Human title of that release, when known.</param>
/// <param name="CheckedAt">When the check ran, or null if it never has.</param>
public sealed record UpdateStatus(
    UpdateState State,
    string? LatestVersion = null,
    string? ReleaseUrl = null,
    string? ReleaseName = null,
    DateTimeOffset? CheckedAt = null)
{
    public static readonly UpdateStatus Disabled = new(UpdateState.Disabled);
    public static readonly UpdateStatus NotChecked = new(UpdateState.NotChecked);
}
