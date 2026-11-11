using System.Globalization;
using QaTracker.Web.Components.Shared;

namespace QaTracker.UnitTests.Components;

public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-11-10T15:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(119, "1 minute ago")]
    [InlineData(45 * 60, "45 minutes ago")]
    [InlineData(59 * 60 + 59, "59 minutes ago")]
    [InlineData(60 * 60, "1 hour ago")]
    [InlineData(89 * 60, "1 hour ago")]
    [InlineData(90 * 60, "2 hours ago")]
    [InlineData(5 * 3600 + 20 * 60, "5 hours ago")]
    [InlineData(15 * 3600, "15 hours ago")] // 00:00 today
    public void Earlier_today_is_relative(int secondsAgo, string expected) =>
        Assert.Equal(expected, RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void Yesterday_is_a_timestamp_even_if_only_minutes_ago()
    {
        var justAfterMidnight = DateTimeOffset.Parse("2026-11-10T00:05:00Z", CultureInfo.InvariantCulture);
        var lateYesterday = DateTimeOffset.Parse("2026-11-09T23:50:00Z", CultureInfo.InvariantCulture);

        Assert.Equal("2026-11-09 23:50 UTC", RelativeTime.Format(lateYesterday, justAfterMidnight));
    }

    [Fact]
    public void Older_dates_are_timestamps() =>
        Assert.Equal("2026-10-01 09:07 UTC",
            RelativeTime.Format(DateTimeOffset.Parse("2026-10-01T09:07:30Z", CultureInfo.InvariantCulture), Now));

    [Fact]
    public void Today_is_the_utc_day_whatever_the_offset()
    {
        // 01:00 on the 11th at +02:00 is 23:00 UTC on the 10th — still "today" in UTC.
        var when = DateTimeOffset.Parse("2026-11-11T01:00:00+02:00", CultureInfo.InvariantCulture);
        var now = DateTimeOffset.Parse("2026-11-10T23:30:00Z", CultureInfo.InvariantCulture);

        Assert.Equal("30 minutes ago", RelativeTime.Format(when, now));
    }

    [Fact]
    public void Slight_clock_skew_into_the_future_reads_just_now() =>
        Assert.Equal("just now", RelativeTime.Format(Now.AddSeconds(5), Now));
}
