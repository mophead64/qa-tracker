namespace QaTracker.Web.Notifications;

/// <summary>
/// The sound choices for the new-notification alert (played client-side by
/// notification-poll.js when a poll turns up a notification the tab hasn't seen yet).
/// Each key is backed by <c>wwwroot/sounds/{Key}.mp3</c>.
/// </summary>
public static class NotificationSounds
{
    public const string Default = "success";

    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("fah", "Fah"),
        ("ding", "Ding"),
        ("success", "Success"),
        ("synth", "Synth"),
    ];

    public static bool IsValid(string? key) => key is not null && All.Any(s => s.Key == key);

    /// <summary>Playback volume, as a percentage of the device volume — the browser can only
    /// scale a sound down, never above what the device is set to.</summary>
    public const int DefaultVolume = 50;

    public static readonly IReadOnlyList<int> Volumes = [25, 50, 75, 100];

    public static bool IsValidVolume(int percent) => Volumes.Contains(percent);
}
