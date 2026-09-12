namespace QaTracker.Web.Notifications;

/// <summary>
/// The sound choices for the new-notification alert (played client-side by
/// notification-poll.js when a poll turns up a notification the tab hasn't seen yet).
/// Each key is backed by <c>wwwroot/sounds/{Key}.mp3</c>.
/// </summary>
public static class NotificationSounds
{
    public const string Default = "fah";

    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("fah", "Fah"),
        ("ding", "Ding"),
        ("success", "Success"),
        ("synth", "Synth"),
    ];

    public static bool IsValid(string? key) => key is not null && All.Any(s => s.Key == key);
}
