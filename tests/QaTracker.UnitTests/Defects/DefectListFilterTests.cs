using QaTracker.Web.Defects;

namespace QaTracker.UnitTests.Defects;

public sealed class DefectListFilterTests
{
    [Theory]
    [InlineData(DefectStatus.NotFixed, true)]
    [InlineData(DefectStatus.Fixing, true)]
    [InlineData(DefectStatus.ToCheck, true)]
    [InlineData(DefectStatus.Fixed, false)]
    [InlineData(DefectStatus.NotADefect, false)]
    public void Open_and_closed_split_every_status_between_them(DefectStatus status, bool open)
    {
        Assert.Equal(open, DefectListFilter.Open.Matches(status));
        Assert.Equal(!open, DefectListFilter.Closed.Matches(status));
    }

    [Fact]
    public void All_matches_every_status() =>
        Assert.All(Enum.GetValues<DefectStatus>(), s => Assert.True(DefectListFilter.All.Matches(s)));

    [Theory]
    [InlineData(DefectListFilter.NotFixed, DefectStatus.NotFixed)]
    [InlineData(DefectListFilter.Fixing, DefectStatus.Fixing)]
    [InlineData(DefectListFilter.ToCheck, DefectStatus.ToCheck)]
    [InlineData(DefectListFilter.Fixed, DefectStatus.Fixed)]
    [InlineData(DefectListFilter.NotADefect, DefectStatus.NotADefect)]
    public void A_single_status_filter_matches_only_that_status(DefectListFilter filter, DefectStatus only)
    {
        Assert.All(Enum.GetValues<DefectStatus>(), s => Assert.Equal(s == only, filter.Matches(s)));
        Assert.Equal(DefectDisplay.StatusLabel(only), DefectListFilters.Label(filter));
    }

    [Fact]
    public void Every_filter_is_offered_once_groups_first()
    {
        Assert.Equal(Enum.GetValues<DefectListFilter>().Order(), DefectListFilters.Ordered.Order());
        Assert.Equal([DefectListFilter.All, DefectListFilter.Open, DefectListFilter.Closed], DefectListFilters.Ordered.Take(3));
        Assert.Equal(DefectListFilters.FirstStatus, DefectListFilters.Ordered[3]);
    }
}
