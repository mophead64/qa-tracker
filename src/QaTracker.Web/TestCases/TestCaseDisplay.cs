namespace QaTracker.Web.TestCases;

/// <summary>Presentation helpers for test case enums.</summary>
public static class TestCaseDisplay
{
    public static string KindLabel(TestCaseKind kind) => kind switch
    {
        TestCaseKind.Functional => "Functional",
        TestCaseKind.NonFunctional => "Non-functional",
        _ => kind.ToString(),
    };

    public static string ResultLabel(TestResult result) => result switch
    {
        TestResult.NotRun => "Not run",
        TestResult.Passed => "Passed",
        TestResult.Failed => "Failed",
        _ => result.ToString(),
    };

    public static string ResultBadgeClass(TestResult result) => result switch
    {
        TestResult.NotRun => "badge badge-gray",
        TestResult.Passed => "badge badge-brand",
        TestResult.Failed => "badge badge-red",
        _ => "badge badge-gray",
    };

    /// <summary>Solid, fully colour-filled chip for the editable result control on the detail page.</summary>
    public static string ResultChipClass(TestResult result) => result switch
    {
        TestResult.NotRun => "chip chip-gray",
        TestResult.Passed => "chip chip-brand",
        TestResult.Failed => "chip chip-red",
        _ => "chip chip-gray",
    };
}
