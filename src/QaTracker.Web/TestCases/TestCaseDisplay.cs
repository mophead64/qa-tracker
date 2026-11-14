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

    /// <summary>"S-3" — a scope's ID within its project.</summary>
    public static string ScopeRef(int number) => $"S-{number}";

    /// <summary>"T-12" — a test case's ID within its project (unique across scopes).</summary>
    public static string CaseRef(int number) => $"T-{number}";

    /// <summary>"T-12 Login works" — a case with its ID, for text that names it (history, notifications).</summary>
    public static string CaseLabel(TestCase @case) =>
        @case.Number > 0 ? $"{CaseRef(@case.Number)} {@case.Scenario.ReplaceLineEndings(" ").Trim()}" : @case.Scenario;

    /// <summary>"S-3 Checkout" — a scope with its ID, for text that names it.</summary>
    public static string ScopeLabel(TestScope scope) => scope.Number > 0 ? $"{ScopeRef(scope.Number)} {scope.Name}" : scope.Name;

    /// <summary>Reads a scope ID: "S-3", "s3", "S 3" or a bare "3".</summary>
    public static bool TryParseScopeRef(string? raw, out int number) => TryParseRef(raw, 'S', out number);

    /// <summary>Reads a test case ID: "T-12", "t12", "T 12" or a bare "12".</summary>
    public static bool TryParseCaseRef(string? raw, out int number) => TryParseRef(raw, 'T', out number);

    private static bool TryParseRef(string? raw, char prefix, out int number)
    {
        number = 0;
        var text = (raw ?? string.Empty).Trim().TrimStart('#');
        if (text.Length > 0 && char.ToUpperInvariant(text[0]) == prefix)
        {
            text = text[1..].TrimStart('-', ' ');
        }

        return int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)
            && number > 0;
    }

    public static string ResultLabel(TestResult result) => result switch
    {
        TestResult.NotRun => "Not run",
        TestResult.Running => "Running",
        TestResult.Passed => "Passed",
        TestResult.Failed => "Failed",
        TestResult.Blocked => "Blocked",
        TestResult.Inconclusive => "Inconclusive",
        _ => result.ToString(),
    };

    public static string ResultBadgeClass(TestResult result) => result switch
    {
        TestResult.NotRun => "badge badge-gray",
        TestResult.Running => "badge badge-purple",
        TestResult.Passed => "badge badge-brand",
        TestResult.Failed => "badge badge-red",
        TestResult.Blocked => "badge badge-orange",
        TestResult.Inconclusive => "badge badge-blue",
        _ => "badge badge-gray",
    };

    /// <summary>Solid, fully colour-filled chip for the editable result control on the detail page.</summary>
    public static string ResultChipClass(TestResult result) => result switch
    {
        TestResult.NotRun => "chip chip-gray",
        TestResult.Running => "chip chip-purple",
        TestResult.Passed => "chip chip-brand",
        TestResult.Failed => "chip chip-red",
        TestResult.Blocked => "chip chip-orange",
        TestResult.Inconclusive => "chip chip-blue",
        _ => "chip chip-gray",
    };
}
