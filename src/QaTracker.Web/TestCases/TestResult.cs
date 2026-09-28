namespace QaTracker.Web.TestCases;

/// <summary>Outcome of the most recent run of a test case.</summary>
public enum TestResult
{
    NotRun = 0,
    Passed = 1,
    Failed = 2,

    /// <summary>Can't be run yet — something else (e.g. another feature) is stopping it. Skipped by test-run navigation.</summary>
    Blocked = 3,

    /// <summary>Ran, but the behaviour raises questions that need business feedback — neither a pass nor a fail. Skipped by test-run navigation.</summary>
    Inconclusive = 4,
}
