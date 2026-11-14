using System.Globalization;

namespace QaTracker.Web.Demo;

/// <summary>
/// The clock the demo seeder runs the app's services on, so every record, history entry and
/// notification gets a believable timestamp. The story is written in its own dates (UTC — London is
/// on UTC in November) and ends at <see cref="StoryNow"/>; <see cref="Offset"/> moves all of it by
/// whole weeks, so a demo seeded today reads as "this week" with the weekdays still right
/// (<see cref="AlignedTo"/>). The docs screenshots use no offset, so they never change.
/// </summary>
public sealed class DemoClock(TimeSpan offset = default) : TimeProvider
{
    /// <summary>The end of the story: Tuesday 10 November 2026, 17:45 UTC. Nothing happens after it.</summary>
    public static readonly DateTimeOffset StoryNow = new(2026, 11, 10, 17, 45, 0, TimeSpan.Zero);

    /// <summary>How far every story moment is moved (whole weeks; zero for the docs screenshots).</summary>
    public TimeSpan Offset { get; } = offset;

    public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>
    /// A clock whose story ends in the last week before <paramref name="now"/>, on the same weekday
    /// and time as the original — so "testing started yesterday" still lands on a Monday.
    /// </summary>
    public static DemoClock AlignedTo(DateTimeOffset now)
    {
        var weeks = (int)Math.Floor((now - StoryNow).TotalDays / 7);
        return new DemoClock(TimeSpan.FromDays(weeks * 7));
    }

    /// <summary>Sets the time to an already-shifted moment (e.g. one from <see cref="Story"/>).</summary>
    public void Set(DateTimeOffset utc) => Now = utc;

    /// <summary>Sets the time to a story moment, shifting it.</summary>
    public void SetStory(DateTimeOffset storyUtc) => Now = storyUtc + Offset;

    /// <summary>"2026-11-09 14:20" (story time, UTC), shifted.</summary>
    public void At(string utc) => Now = Story(utc);

    public void Tick(int minutes = 1) => Now = Now.AddMinutes(minutes);

    /// <summary>A story moment ("2026-11-09 14:20"), shifted.</summary>
    public DateTimeOffset Story(string utc) => Parse(utc) + Offset;

    /// <summary>A story day (testing windows), shifted.</summary>
    public DateOnly Day(DateOnly storyDay) => storyDay.AddDays((int)Offset.TotalDays);

    /// <summary>"2026-11-09 14:20" as UTC, unshifted.</summary>
    public static DateTimeOffset Parse(string utc) =>
        new(DateTime.ParseExact(utc, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), TimeSpan.Zero);
}
