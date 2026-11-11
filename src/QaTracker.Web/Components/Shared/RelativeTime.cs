using System.Globalization;

namespace QaTracker.Web.Components.Shared;

/// <summary>
/// "When" labels for timelines: relative for anything earlier today ("just now", "12 minutes
/// ago", "3 hours ago" — hours rounded to the nearest), an absolute timestamp for anything
/// older. "Today" is the UTC day, matching the UTC timestamps shown everywhere else in the app.
/// </summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset when, DateTimeOffset now)
    {
        var whenUtc = when.UtcDateTime;
        var nowUtc = now.UtcDateTime;
        if (whenUtc.Date != nowUtc.Date)
        {
            return Timestamp(when);
        }

        var elapsed = nowUtc - whenUtc;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            // Includes small clock skew putting the event fractionally in the future.
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return Plural((int)elapsed.TotalMinutes, "minute") + " ago";
        }

        var hours = (int)Math.Round(elapsed.TotalHours, MidpointRounding.AwayFromZero);
        return Plural(hours, "hour") + " ago";
    }

    /// <summary>The app's standard absolute timestamp, e.g. "2026-11-09 14:32 UTC".</summary>
    public static string Timestamp(DateTimeOffset when) =>
        when.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
