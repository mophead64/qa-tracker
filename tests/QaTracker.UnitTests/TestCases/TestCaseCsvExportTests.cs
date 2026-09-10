using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

public sealed class TestCaseCsvExportTests
{
    private static readonly Dictionary<Guid, IReadOnlyList<int>> NoDefects = new();

    private static TestScope Scope(string name, TestCaseKind kind, params TestCase[] cases) =>
        new() { Id = Guid.NewGuid(), Name = name, Kind = kind, Cases = [.. cases] };

    private static TestCase Case(string scenario, string? steps) =>
        new() { Id = Guid.NewGuid(), Scenario = scenario, Steps = steps };

    [Fact]
    public void Export_starts_with_the_shared_import_header()
    {
        var csv = TestCaseCsv.Export([Scope("Auth", TestCaseKind.Functional, Case("Login works", "do"))], NoDefects);

        Assert.StartsWith(TestCaseCsv.Header + "\n", csv);
    }

    [Fact]
    public void Export_repeats_scope_type_scenario_only_on_the_first_row_of_a_case()
    {
        var csv = TestCaseCsv.Export(
            [Scope("Auth", TestCaseKind.Functional, Case("Login works", "step one\nstep two\nstep three"))],
            NoDefects);

        var rows = csv.TrimEnd('\n').Split('\n');
        Assert.Equal("Auth,Functional,Login works,step one,", rows[1]);
        Assert.Equal(",,,step two,", rows[2]);
        Assert.Equal(",,,step three,", rows[3]);
    }

    [Fact]
    public void Export_of_a_stepless_case_is_a_single_row()
    {
        var csv = TestCaseCsv.Export(
            [Scope("Perf", TestCaseKind.NonFunctional, Case("Fast search", null))],
            NoDefects);

        var rows = csv.TrimEnd('\n').Split('\n');
        Assert.Equal(2, rows.Length);
        Assert.Equal("Perf,Non-Functional,Fast search,,", rows[1]);
    }

    [Fact]
    public void Export_spreads_defect_links_across_rows_alongside_steps()
    {
        var @case = Case("Login works", "step one");
        var defects = new Dictionary<Guid, IReadOnlyList<int>> { [@case.Id] = [2, 5] };

        var csv = TestCaseCsv.Export([Scope("Auth", TestCaseKind.Functional, @case)], defects);

        var rows = csv.TrimEnd('\n').Split('\n');
        Assert.Equal("Auth,Functional,Login works,step one,D-2", rows[1]);
        Assert.Equal(",,,,D-5", rows[2]);
    }

    [Fact]
    public void Export_quotes_fields_that_contain_commas()
    {
        var csv = TestCaseCsv.Export(
            [Scope("Auth", TestCaseKind.Functional, Case("Login fails, then locks", "do"))],
            NoDefects);

        Assert.Contains("\"Login fails, then locks\"", csv);
    }

    [Fact]
    public void Export_round_trips_through_the_importer()
    {
        var loginCase = Case("Login works", "open the app\nenter credentials\nsubmit");
        var lockoutCase = Case("Locks after 3 fails", "fail three times");
        var perfCase = Case("Search is fast", null);
        var defects = new Dictionary<Guid, IReadOnlyList<int>>
        {
            [loginCase.Id] = [1],
            [lockoutCase.Id] = [1, 4],
        };

        var scopes = new[]
        {
            Scope("Authentication", TestCaseKind.Functional, loginCase, lockoutCase),
            Scope("Performance", TestCaseKind.NonFunctional, perfCase),
        };

        var csv = TestCaseCsv.Export(scopes, defects);
        var parsed = TestCaseImportParser.Parse(csv);

        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        Assert.Equal(["Authentication", "Performance"], parsed.Scopes.Select(s => s.Name));
        Assert.Equal(
            [TestCaseKind.Functional, TestCaseKind.NonFunctional],
            parsed.Scopes.Select(s => s.Kind));

        var auth = parsed.Scopes[0];
        Assert.Equal(["Login works", "Locks after 3 fails"], auth.Cases.Select(c => c.Scenario));
        Assert.Equal("open the app\nenter credentials\nsubmit", auth.Cases[0].Steps);
        Assert.Equal(["D-1"], auth.Cases[0].DefectTokens);
        Assert.Equal(["D-1", "D-4"], auth.Cases[1].DefectTokens);

        Assert.Null(parsed.Scopes[1].Cases.Single().Steps);
    }
}
