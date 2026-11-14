namespace QaTracker.Web.TestCases;

/// <summary>Which test cases the test cases page lists. Stored per user (like
/// <see cref="Projects.ProjectListFilter"/>), so the choice follows them across projects and logins.</summary>
public enum TestCaseListFilter
{
    /// <summary>Every test case. The default.</summary>
    All = 0,

    /// <summary>Still needs work: Not run, Running, Failed or Blocked. Excludes Passed and Inconclusive
    /// (the latter is waiting on business feedback rather than on testing).</summary>
    Outstanding = 1,

    Passed = 2,
    Failed = 3,
    Blocked = 4,
    Inconclusive = 5,
    NotRun = 6,
    Running = 7,
}

public static class TestCaseListFilters
{
    /// <summary>In the order the dropdown offers them: All, Outstanding, then each result.</summary>
    public static readonly IReadOnlyList<TestCaseListFilter> Ordered =
    [
        TestCaseListFilter.All,
        TestCaseListFilter.Outstanding,
        TestCaseListFilter.Running,
        TestCaseListFilter.Passed,
        TestCaseListFilter.Failed,
        TestCaseListFilter.Blocked,
        TestCaseListFilter.Inconclusive,
        TestCaseListFilter.NotRun,
    ];

    public static string Label(TestCaseListFilter filter) => filter switch
    {
        TestCaseListFilter.All => "All test cases",
        TestCaseListFilter.Outstanding => "Outstanding",
        TestCaseListFilter.Passed => TestCaseDisplay.ResultLabel(TestResult.Passed),
        TestCaseListFilter.Failed => TestCaseDisplay.ResultLabel(TestResult.Failed),
        TestCaseListFilter.Blocked => TestCaseDisplay.ResultLabel(TestResult.Blocked),
        TestCaseListFilter.Inconclusive => TestCaseDisplay.ResultLabel(TestResult.Inconclusive),
        TestCaseListFilter.NotRun => TestCaseDisplay.ResultLabel(TestResult.NotRun),
        TestCaseListFilter.Running => TestCaseDisplay.ResultLabel(TestResult.Running),
        _ => filter.ToString(),
    };

    /// <summary>A short hint shown beside the option, for the grouped filters.</summary>
    public static string? Hint(TestCaseListFilter filter) => filter switch
    {
        TestCaseListFilter.Outstanding => "Not run, running, failed or blocked",
        _ => null,
    };

    public static bool Matches(this TestCaseListFilter filter, TestResult result) => filter switch
    {
        TestCaseListFilter.All => true,
        TestCaseListFilter.Outstanding => result is TestResult.NotRun or TestResult.Running or TestResult.Failed or TestResult.Blocked,
        TestCaseListFilter.Passed => result == TestResult.Passed,
        TestCaseListFilter.Failed => result == TestResult.Failed,
        TestCaseListFilter.Blocked => result == TestResult.Blocked,
        TestCaseListFilter.Inconclusive => result == TestResult.Inconclusive,
        TestCaseListFilter.NotRun => result == TestResult.NotRun,
        TestCaseListFilter.Running => result == TestResult.Running,
        _ => true,
    };
}
