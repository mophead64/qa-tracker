using QaTracker.Web.Demo;

namespace QaTracker.UnitTests.Demo;

/// <summary>The demo story is shifted by whole weeks so it reads as "this week" with the weekdays intact.</summary>
public sealed class DemoClockTests
{
    [Theory]
    [InlineData("2026-11-10T17:45:00Z", 0)]   // the story's own "now"
    [InlineData("2026-11-17T17:44:00Z", 0)]   // not quite a week later: still last Tuesday
    [InlineData("2026-11-17T17:45:00Z", 7)]
    [InlineData("2027-03-01T09:00:00Z", 105)] // a Monday: the story's Tuesday was 6 days ago
    [InlineData("2026-06-01T09:00:00Z", -168)]
    public void The_story_ends_within_the_week_before_now_on_the_same_weekday(string now, int expectedDays)
    {
        var at = DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture);
        var clock = DemoClock.AlignedTo(at);

        Assert.Equal(TimeSpan.FromDays(expectedDays), clock.Offset);
        var shiftedNow = DemoClock.StoryNow + clock.Offset;
        Assert.True(shiftedNow <= at && at - shiftedNow < TimeSpan.FromDays(7));
        Assert.Equal(DemoClock.StoryNow.DayOfWeek, shiftedNow.DayOfWeek);
    }

    [Fact]
    public void Story_times_and_days_move_by_the_offset()
    {
        var clock = new DemoClock(TimeSpan.FromDays(14));

        clock.At("2026-11-09 09:05");
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 9, 5, 0, TimeSpan.Zero), clock.GetUtcNow());
        Assert.Equal(new DateOnly(2026, 11, 27), clock.Day(new DateOnly(2026, 11, 13)));

        clock.SetStory(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), clock.GetUtcNow());
    }

    [Fact]
    public void Every_sample_the_story_attaches_is_embedded()
    {
        string[] files =
        [
            "promo-code-expired.png", "keyboard-focus-lost.png", "delivery-slots-after-cutoff.png", "invoice-vat.png",
            "checkout-flow.png", "basket-second-device.png", "harbourline-test-plan.pdf", "checkout-performance-report.pdf",
            "sign-off-report.pdf", "3ds-challenge-loop.webm", "safari-console.log", "test-accounts.csv", "network-trace.har",
            "promo-codes.csv",
        ];
        foreach (var file in files)
        {
            using var stream = DemoSamples.Open(file);
            Assert.True(stream.Length > 0, file);
        }
    }
}
