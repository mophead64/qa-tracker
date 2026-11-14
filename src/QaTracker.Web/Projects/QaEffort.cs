using System.Globalization;

namespace QaTracker.Web.Projects;

/// <summary>A project's testing window and effort estimates, as entered on the project form.</summary>
/// <param name="TestingStart">First day of testing.</param>
/// <param name="TestingEnd">Last day of testing (the same as <paramref name="TestingStart"/> for a one-day window).</param>
/// <param name="StartsMidday">Testing starts at midday, so the first day counts as half.</param>
/// <param name="EndsMidday">Testing ends at midday, so the last day counts as half.</param>
/// <param name="EstimatedDevDays">Estimated development effort, in days.</param>
/// <param name="EstimatedQaDays">A QA estimate typed in directly — wins over the percentage.</param>
/// <param name="QaPercent">This project's QA-as-a-percentage-of-dev, overriding the system default.</param>
public sealed record ProjectSchedule(
    DateOnly? TestingStart,
    DateOnly? TestingEnd,
    bool StartsMidday,
    bool EndsMidday,
    decimal? EstimatedDevDays,
    decimal? EstimatedQaDays,
    int? QaPercent)
{
    public static readonly ProjectSchedule Empty = new(null, null, false, false, null, null, null);

    public static ProjectSchedule From(Project p) => new(
        p.TestingStartDate, p.TestingEndDate, p.TestingStartsMidday, p.TestingEndsMidday,
        p.EstimatedDevDays, p.EstimatedQaDays, p.QaEffortPercent);
}

/// <summary>What the "(x days)" label beside a project's status shows.</summary>
/// <param name="Days">QA days: the testing window's length, or the estimate when there's no window.</param>
/// <param name="IsEstimate">True when <paramref name="Days"/> is the estimate rather than a scheduled window.</param>
public sealed record QaDaysLabel(decimal Days, bool IsEstimate)
{
    public string Text => (IsEstimate ? "~" : "") + QaEffort.FormatDays(Days);
}

/// <summary>
/// QA effort arithmetic. Days are counted in half-day steps over weekdays only (Mon–Fri;
/// bank holidays aren't known, so they still count). The QA estimate is a typed figure if
/// there is one, else estimated dev days × the QA percentage (the project's own, or the
/// system default), rounded to the nearest half day.
/// </summary>
public static class QaEffort
{
    /// <summary>System-wide QA percentage for a fresh database (also the seed default).</summary>
    public const int DefaultPercent = 25;

    public const int MinPercent = 1;
    public const int MaxPercent = 100;

    /// <summary>Upper bound for a typed day figure — far beyond any real estimate.</summary>
    public const decimal MaxDays = 9999m;

    /// <summary>
    /// Weekdays from <paramref name="start"/> to <paramref name="end"/> inclusive, less half a
    /// day for each midday flag (only when that day is itself a weekday). Null when either
    /// date is missing or the window runs backwards.
    /// </summary>
    public static decimal? TestingDays(DateOnly? start, DateOnly? end, bool startsMidday, bool endsMidday)
    {
        if (start is not { } from || end is not { } to || to < from)
        {
            return null;
        }

        decimal days = Weekdays(from, to);
        if (startsMidday && IsWeekday(from))
        {
            days -= 0.5m;
        }
        if (endsMidday && IsWeekday(to))
        {
            days -= 0.5m;
        }
        return Math.Max(0m, days);
    }

    /// <summary>The QA estimate: the typed figure, else dev days × percent (nearest half day, at least half a day).</summary>
    public static decimal? EstimatedQaDays(decimal? devDays, decimal? typedQaDays, int percent)
    {
        if (typedQaDays is > 0)
        {
            return typedQaDays;
        }

        if (devDays is not > 0)
        {
            return null;
        }

        var raw = devDays.Value * percent / 100m;
        return Math.Max(0.5m, Math.Round(raw * 2, MidpointRounding.AwayFromZero) / 2);
    }

    /// <summary>The percentage in force for a project: its own override, else the system default.</summary>
    public static int EffectivePercent(int? projectPercent, int systemPercent) =>
        projectPercent ?? systemPercent;

    /// <summary>The label beside the project status, or null when there's nothing to show.</summary>
    public static QaDaysLabel? Label(ProjectSchedule s, int systemPercent)
    {
        if (TestingDays(s.TestingStart, s.TestingEnd, s.StartsMidday, s.EndsMidday) is { } scheduled)
        {
            return new QaDaysLabel(scheduled, IsEstimate: false);
        }

        var estimate = EstimatedQaDays(s.EstimatedDevDays, s.EstimatedQaDays, EffectivePercent(s.QaPercent, systemPercent));
        return estimate is { } days ? new QaDaysLabel(days, IsEstimate: true) : null;
    }

    public static QaDaysLabel? Label(Project p, int systemPercent) => Label(ProjectSchedule.From(p), systemPercent);

    /// <summary>
    /// The testing window for the dashboard, e.g. "Mon 9 Nov 2026 (from midday) – Wed 11 Nov 2026",
    /// or a single date for a one-day window. Null when there's no window.
    /// </summary>
    public static string? WindowText(ProjectSchedule s)
    {
        if (s.TestingStart is not { } start || s.TestingEnd is not { } end)
        {
            return null;
        }

        static string Day(DateOnly d) => d.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);

        if (start == end)
        {
            if (s.StartsMidday)
            {
                return Day(start) + " (afternoon)";
            }
            return Day(start) + (s.EndsMidday ? " (morning)" : "");
        }

        return Day(start) + (s.StartsMidday ? " (from midday)" : "")
            + " – " + Day(end) + (s.EndsMidday ? " (until midday)" : "");
    }

    /// <summary>
    /// The testing window for the All projects list: "9 Nov – 11 Nov", or "9 Nov" for a one-day
    /// window. Years are only shown when a date falls outside <paramref name="currentYear"/>
    /// ("28 Dec 2026 – 8 Jan 2027"). Midday flags are left to the tooltip (<see cref="WindowText"/>).
    /// Null when there's no window.
    /// </summary>
    public static string? ShortWindowText(ProjectSchedule s, int currentYear)
    {
        if (s.TestingStart is not { } start || s.TestingEnd is not { } end)
        {
            return null;
        }

        var format = start.Year == currentYear && end.Year == currentYear ? "d MMM" : "d MMM yyyy";
        string Day(DateOnly d) => d.ToString(format, CultureInfo.InvariantCulture);

        return start == end ? Day(start) : Day(start) + " – " + Day(end);
    }

    /// <summary>
    /// The estimate for the dashboard: "10 dev days → 2.5 QA days (25%)" when worked out from
    /// the percentage, "10 dev days · 3 QA days" when typed. Null when there's no estimate.
    /// </summary>
    public static string? EstimateText(ProjectSchedule s, int systemPercent)
    {
        var percent = EffectivePercent(s.QaPercent, systemPercent);
        var qa = EstimatedQaDays(s.EstimatedDevDays, s.EstimatedQaDays, percent);
        var dev = s.EstimatedDevDays is { } d ? $"{FormatDays(d)} dev" : null;

        if (qa is null)
        {
            return dev;
        }

        var qaText = $"{FormatDays(qa.Value)} QA";
        if (s.EstimatedQaDays is > 0)
        {
            return dev is null ? qaText : $"{dev} · {qaText}";
        }

        var source = s.QaPercent is null ? $"{percent}%, system default" : $"{percent}%";
        return $"{dev} → {qaText} ({source})";
    }

    /// <summary>"1 day", "0.5 days", "2.5 days", "12 days".</summary>
    public static string FormatDays(decimal days) =>
        days == 1m ? "1 day" : $"{days.ToString("0.#", CultureInfo.InvariantCulture)} days";

    /// <summary>
    /// The first problem with a schedule, or null when it's fine. Messages name the field so the
    /// form can show them beside it.
    /// </summary>
    public static string? Validate(ProjectSchedule s)
    {
        if (s.TestingStart is null != s.TestingEnd is null)
        {
            return "Set both testing dates, or neither.";
        }

        if (s.TestingStart is { } start && s.TestingEnd is { } end)
        {
            if (end < start)
            {
                return "The testing end date can't be before the start date.";
            }

            if (Weekdays(start, end) == 0)
            {
                return "The testing window has no weekdays in it.";
            }

            if (TestingDays(start, end, s.StartsMidday, s.EndsMidday) is not > 0)
            {
                return "A one-day window can't both start and end at midday — untick one for a half day.";
            }
        }

        return DaysError(s.EstimatedDevDays, "Estimated dev days")
            ?? DaysError(s.EstimatedQaDays, "Estimated QA days")
            ?? (s.QaPercent is { } pct && (pct < MinPercent || pct > MaxPercent)
                ? $"QA percentage must be between {MinPercent} and {MaxPercent}."
                : null);
    }

    private static string? DaysError(decimal? days, string field) => days switch
    {
        null => null,
        <= 0 => $"{field} must be more than 0.",
        > MaxDays => $"{field} can be at most {MaxDays:0}.",
        _ when days.Value * 2 != decimal.Truncate(days.Value * 2) => $"{field} must be in half-day steps (e.g. 2 or 2.5).",
        _ => null,
    };

    private static bool IsWeekday(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    // Whole weeks contribute 5 each; walk the remaining (< 7) days.
    private static int Weekdays(DateOnly from, DateOnly to)
    {
        var total = to.DayNumber - from.DayNumber + 1;
        var count = total / 7 * 5;
        for (var d = from.AddDays(total / 7 * 7); d <= to; d = d.AddDays(1))
        {
            if (IsWeekday(d))
            {
                count++;
            }
        }
        return count;
    }
}
