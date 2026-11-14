using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

public sealed class TestCaseListFilterTests
{
    [Theory]
    [InlineData(TestResult.NotRun, true)]
    [InlineData(TestResult.Running, true)]
    [InlineData(TestResult.Failed, true)]
    [InlineData(TestResult.Blocked, true)]
    [InlineData(TestResult.Passed, false)]
    [InlineData(TestResult.Inconclusive, false)]
    public void Outstanding_is_not_run_running_failed_or_blocked(TestResult result, bool expected) =>
        Assert.Equal(expected, TestCaseListFilter.Outstanding.Matches(result));

    [Fact]
    public void All_matches_every_result() =>
        Assert.All(Enum.GetValues<TestResult>(), r => Assert.True(TestCaseListFilter.All.Matches(r)));

    [Theory]
    [InlineData(TestCaseListFilter.Passed, TestResult.Passed)]
    [InlineData(TestCaseListFilter.Failed, TestResult.Failed)]
    [InlineData(TestCaseListFilter.Blocked, TestResult.Blocked)]
    [InlineData(TestCaseListFilter.Inconclusive, TestResult.Inconclusive)]
    [InlineData(TestCaseListFilter.NotRun, TestResult.NotRun)]
    [InlineData(TestCaseListFilter.Running, TestResult.Running)]
    public void A_single_result_filter_matches_only_that_result(TestCaseListFilter filter, TestResult only) =>
        Assert.All(Enum.GetValues<TestResult>(), r => Assert.Equal(r == only, filter.Matches(r)));

    [Fact]
    public void Every_filter_is_offered_once_starting_with_all_then_outstanding()
    {
        Assert.Equal(Enum.GetValues<TestCaseListFilter>().Order(), TestCaseListFilters.Ordered.Order());
        Assert.Equal([TestCaseListFilter.All, TestCaseListFilter.Outstanding], TestCaseListFilters.Ordered.Take(2));
    }

    [Fact]
    public void Single_result_filters_use_the_result_labels()
    {
        Assert.Equal("Not run", TestCaseListFilters.Label(TestCaseListFilter.NotRun));
        Assert.Equal("All test cases", TestCaseListFilters.Label(TestCaseListFilter.All));
        Assert.Equal("Outstanding", TestCaseListFilters.Label(TestCaseListFilter.Outstanding));
    }
}
