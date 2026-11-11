using QaTracker.Web.Projects;

namespace QaTracker.UnitTests.Projects;

public sealed class QaEffortTests
{
    // Week of Mon 9 Nov 2026 – Fri 13 Nov; Sat 14 / Sun 15; Mon 16.
    private static DateOnly D(int day) => new(2026, 11, day);

    private static ProjectSchedule Window(int start, int end, bool startsMidday = false, bool endsMidday = false) =>
        ProjectSchedule.Empty with
        {
            TestingStart = D(start), TestingEnd = D(end), StartsMidday = startsMidday, EndsMidday = endsMidday,
        };

    // ---- testing days -----------------------------------------------------------------

    [Theory]
    [InlineData(9, 9, false, false, 1.0)]   // one day
    [InlineData(9, 9, false, true, 0.5)]    // half a day (morning)
    [InlineData(9, 9, true, false, 0.5)]    // half a day (afternoon)
    [InlineData(9, 11, false, false, 3.0)]  // Mon–Wed
    [InlineData(9, 11, false, true, 2.5)]   // Mon–Wed, ending at midday
    [InlineData(9, 11, true, true, 2.0)]    // starts and ends at midday
    [InlineData(13, 16, false, false, 2.0)] // Fri–Mon: the weekend doesn't count
    [InlineData(9, 20, false, false, 10.0)] // two full weeks
    [InlineData(9, 23, false, false, 11.0)] // two weeks + a Monday
    [InlineData(14, 16, true, false, 1.0)]  // starts on a Saturday: its midday flag takes nothing off
    public void Testing_days_count_weekdays_in_half_day_steps(int start, int end, bool startsMidday, bool endsMidday, double expected)
    {
        Assert.Equal((decimal)expected, QaEffort.TestingDays(D(start), D(end), startsMidday, endsMidday));
    }

    [Fact]
    public void Testing_days_need_both_dates_the_right_way_round()
    {
        Assert.Null(QaEffort.TestingDays(D(9), null, false, false));
        Assert.Null(QaEffort.TestingDays(null, D(9), false, false));
        Assert.Null(QaEffort.TestingDays(D(11), D(9), false, false));
    }

    // ---- estimate ---------------------------------------------------------------------

    [Theory]
    [InlineData(10.0, 25, 2.5)]
    [InlineData(7.0, 25, 2.0)]  // 1.75 → nearest half day
    [InlineData(3.0, 25, 1.0)]  // 0.75 → 1
    [InlineData(1.0, 25, 0.5)]  // 0.25 → never less than half a day
    [InlineData(10.0, 40, 4.0)]
    public void Estimate_is_dev_days_times_the_percent_to_the_nearest_half_day(double dev, int percent, double expected)
    {
        Assert.Equal((decimal)expected, QaEffort.EstimatedQaDays((decimal)dev, null, percent));
    }

    [Fact]
    public void A_typed_QA_estimate_wins_over_the_percentage()
    {
        Assert.Equal(3m, QaEffort.EstimatedQaDays(10m, 3m, 25));
        Assert.Equal(3m, QaEffort.EstimatedQaDays(null, 3m, 25));
    }

    [Fact]
    public void No_dev_days_and_nothing_typed_means_no_estimate()
    {
        Assert.Null(QaEffort.EstimatedQaDays(null, null, 25));
    }

    [Fact]
    public void A_project_percent_overrides_the_system_default()
    {
        Assert.Equal(40, QaEffort.EffectivePercent(40, 25));
        Assert.Equal(25, QaEffort.EffectivePercent(null, 25));
    }

    // ---- label ------------------------------------------------------------------------

    [Fact]
    public void Label_shows_the_testing_window_when_dates_are_set()
    {
        var label = QaEffort.Label(Window(9, 11, endsMidday: true) with { EstimatedDevDays = 40m }, 25);

        Assert.Equal(new QaDaysLabel(2.5m, IsEstimate: false), label);
        Assert.Equal("2.5 days", label!.Text);
    }

    [Fact]
    public void Label_falls_back_to_the_estimate_marked_approximate()
    {
        var label = QaEffort.Label(ProjectSchedule.Empty with { EstimatedDevDays = 10m }, 25);

        Assert.Equal("~2.5 days", label!.Text);
        Assert.True(label.IsEstimate);
    }

    [Fact]
    public void Label_is_null_with_nothing_set()
    {
        Assert.Null(QaEffort.Label(ProjectSchedule.Empty, 25));
    }

    [Theory]
    [InlineData(1.0, "1 day")]
    [InlineData(0.5, "0.5 days")]
    [InlineData(2.0, "2 days")]
    [InlineData(12.5, "12.5 days")]
    public void Days_format(double days, string expected)
    {
        Assert.Equal(expected, QaEffort.FormatDays((decimal)days));
    }

    // ---- dashboard text ---------------------------------------------------------------

    [Fact]
    public void Window_text_describes_the_dates_and_midday_flags()
    {
        Assert.Equal("Mon 9 Nov 2026 (from midday) – Wed 11 Nov 2026 (until midday)",
            QaEffort.WindowText(Window(9, 11, startsMidday: true, endsMidday: true)));
        Assert.Equal("Mon 9 Nov 2026 (morning)", QaEffort.WindowText(Window(9, 9, endsMidday: true)));
        Assert.Equal("Mon 9 Nov 2026", QaEffort.WindowText(Window(9, 9)));
        Assert.Null(QaEffort.WindowText(ProjectSchedule.Empty));
    }

    [Fact]
    public void Estimate_text_says_where_the_QA_figure_came_from()
    {
        Assert.Equal("10 days dev → 2.5 days QA (25%, system default)",
            QaEffort.EstimateText(ProjectSchedule.Empty with { EstimatedDevDays = 10m }, 25));
        Assert.Equal("10 days dev → 4 days QA (40%)",
            QaEffort.EstimateText(ProjectSchedule.Empty with { EstimatedDevDays = 10m, QaPercent = 40 }, 25));
        Assert.Equal("10 days dev · 3 days QA",
            QaEffort.EstimateText(ProjectSchedule.Empty with { EstimatedDevDays = 10m, EstimatedQaDays = 3m }, 25));
        Assert.Null(QaEffort.EstimateText(ProjectSchedule.Empty, 25));
    }

    // ---- validation -------------------------------------------------------------------

    [Fact]
    public void A_complete_schedule_is_valid()
    {
        Assert.Null(QaEffort.Validate(Window(9, 11, endsMidday: true) with { EstimatedDevDays = 10.5m, QaPercent = 30 }));
        Assert.Null(QaEffort.Validate(ProjectSchedule.Empty));
    }

    [Fact]
    public void Validation_rejects_bad_windows()
    {
        Assert.Contains("both testing dates", QaEffort.Validate(ProjectSchedule.Empty with { TestingStart = D(9) }));
        Assert.Contains("before the start", QaEffort.Validate(Window(11, 9)));
        Assert.Contains("no weekdays", QaEffort.Validate(Window(14, 15)));
        Assert.Contains("untick one", QaEffort.Validate(Window(9, 9, startsMidday: true, endsMidday: true)));
    }

    [Theory]
    [InlineData(0.0, "more than 0")]
    [InlineData(-1.0, "more than 0")]
    [InlineData(2.3, "half-day steps")]
    [InlineData(10000.0, "at most")]
    public void Validation_rejects_bad_day_figures(double dev, string message)
    {
        Assert.Contains(message, QaEffort.Validate(ProjectSchedule.Empty with { EstimatedDevDays = (decimal)dev }));
        Assert.Contains(message, QaEffort.Validate(ProjectSchedule.Empty with { EstimatedQaDays = (decimal)dev }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validation_rejects_a_percent_out_of_range(int percent)
    {
        Assert.Contains("between 1 and 100", QaEffort.Validate(ProjectSchedule.Empty with { QaPercent = percent }));
    }
}
