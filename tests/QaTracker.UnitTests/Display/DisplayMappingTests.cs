using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.Tags;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Display;

/// <summary>
/// The label / colour maps behind every badge and chip: each enum value has its own wording and a
/// real class, so adding a status can't silently fall through to a default (the raw enum name, or
/// a grey nothing) somewhere in the UI.
/// </summary>
public sealed class DisplayMappingTests
{
    private static void AssertDistinctLabels<T>(Func<T, string> label) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var labels = values.Select(label).ToList();
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.Equal(values.Length, labels.Distinct().Count());
    }

    private static void AssertClasses<T>(Func<T, string> css, string prefix) where T : struct, Enum =>
        Assert.All(Enum.GetValues<T>(), v => Assert.StartsWith(prefix, css(v)));

    [Fact]
    public void Defect_statuses_and_severities_each_have_their_own_label_and_colours()
    {
        AssertDistinctLabels<DefectStatus>(DefectDisplay.StatusLabel);
        AssertDistinctLabels<DefectSeverity>(DefectDisplay.SeverityLabel);
        AssertClasses<DefectStatus>(DefectDisplay.StatusBadgeClass, "badge");
        AssertClasses<DefectStatus>(DefectDisplay.StatusChipClass, "chip");
        AssertClasses<DefectSeverity>(DefectDisplay.SeverityBadgeClass, "badge");
        AssertClasses<DefectSeverity>(DefectDisplay.SeverityChipClass, "chip");
        // Labels read as words, not enum names.
        Assert.Equal("Not a defect", DefectDisplay.StatusLabel(DefectStatus.NotADefect));
        Assert.Equal("To check", DefectDisplay.StatusLabel(DefectStatus.ToCheck));
    }

    [Fact]
    public void Test_results_and_kinds_each_have_their_own_label_and_colours()
    {
        AssertDistinctLabels<TestResult>(TestCaseDisplay.ResultLabel);
        AssertDistinctLabels<TestCaseKind>(TestCaseDisplay.KindLabel);
        AssertClasses<TestResult>(TestCaseDisplay.ResultBadgeClass, "badge");
        AssertClasses<TestResult>(TestCaseDisplay.ResultChipClass, "chip");
        Assert.Equal("Not run", TestCaseDisplay.ResultLabel(TestResult.NotRun));
    }

    [Fact]
    public void Project_statuses_are_all_listed_in_order_with_sentence_case_labels()
    {
        Assert.Equal(Enum.GetValues<ProjectStatus>().Order(), ProjectStatusDisplay.InOrder.Order());
        AssertDistinctLabels<ProjectStatus>(ProjectStatusDisplay.Label);
        AssertClasses<ProjectStatus>(ProjectStatusDisplay.BadgeClass, "badge");
        AssertClasses<ProjectStatus>(ProjectStatusDisplay.ChipClass, "chip");
        Assert.Equal("Not started", ProjectStatusDisplay.Label(ProjectStatus.NotStarted));
        Assert.All(Enum.GetValues<ProjectStatus>(), s =>
        {
            var label = ProjectStatusDisplay.Label(s);
            Assert.Equal(label[..1].ToUpperInvariant() + label[1..].ToLowerInvariant(), label);
        });
    }

    [Theory]
    [InlineData(ProjectStatus.NotStarted, true)]
    [InlineData(ProjectStatus.Upcoming, true)]
    [InlineData(ProjectStatus.InFlight, true)]
    [InlineData(ProjectStatus.Complete, false)]
    [InlineData(ProjectStatus.Cancelled, false)]
    public void Open_projects_are_the_ones_still_to_do_or_in_progress(ProjectStatus status, bool open) =>
        Assert.Equal(open, ProjectStatusDisplay.IsOpen(status));

    [Fact]
    public void Every_tag_colour_has_a_badge_and_a_swatch()
    {
        Assert.Equal(Enum.GetValues<TagColour>(), TagDisplay.Colours);
        Assert.Equal(TagDisplay.Colours.Count, TagDisplay.Colours.Select(TagDisplay.BadgeClass).Distinct().Count());
        Assert.Equal(TagDisplay.Colours.Count, TagDisplay.Colours.Select(TagDisplay.SwatchClass).Distinct().Count());
    }

    [Theory]
    [InlineData("T-12", true, 12)]
    [InlineData("t12", true, 12)]
    [InlineData("#T-3", true, 3)]
    [InlineData(" 7 ", true, 7)]
    [InlineData("T-0", false, 0)]
    [InlineData("S-1", false, 0)]
    [InlineData("T--1", true, 1)]
    [InlineData("T-1a", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void Case_ids_parse_with_or_without_the_prefix(string? raw, bool ok, int number)
    {
        Assert.Equal(ok, TestCaseDisplay.TryParseCaseRef(raw, out var parsed));
        Assert.Equal(number, ok ? parsed : 0);
    }

    [Fact]
    public void Ids_format_with_their_letter()
    {
        Assert.Equal("D-4", DefectDisplay.Ref(4));
        Assert.Equal("S-2", TestCaseDisplay.ScopeRef(2));
        Assert.Equal("T-9", TestCaseDisplay.CaseRef(9));
        Assert.True(TestCaseDisplay.TryParseScopeRef("s-2", out var scope) && scope == 2);
    }
}
