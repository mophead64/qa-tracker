using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

public sealed class TestCaseImportParserTests
{
    // The example shipped in the repo root: a sparse block (Registration), a dense block
    // (Authentication), and two step-less non-functional cases with different type spellings.
    private const string ExampleCsv =
        "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
        "Registration,Functional,\"Users are not able to register accounts, when an account with that email already exists\",Open the app,D-1\n" +
        ",,,Go to the login page,\n" +
        ",,,Click register,\n" +
        ",,,enter an email that hasn't been used,\n" +
        "Authentication,Functional,Users cannot login after 3 failed attempts,Open the app,\n" +
        "Authentication,Functional,Users cannot login after 3 failed attempts,click login,\n" +
        "Authentication,Functional,Users cannot login after 3 failed attempts,repeat 3x,D-2\n" +
        "Storage,Non-Functional,Users can securely upload files,,\n" +
        "Jobs,Non Functional,Users can submit 100k jobs,,\n";

    [Fact]
    public void Parses_the_example_file_into_four_scopes()
    {
        var result = TestCaseImportParser.Parse(ExampleCsv);

        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Equal(["Registration", "Authentication", "Storage", "Jobs"], result.Scopes.Select(s => s.Name));
        Assert.Equal(
            [TestCaseKind.Functional, TestCaseKind.Functional, TestCaseKind.NonFunctional, TestCaseKind.NonFunctional],
            result.Scopes.Select(s => s.Kind));
    }

    [Fact]
    public void Sparse_block_gathers_continuation_rows_into_one_case_with_multiline_steps()
    {
        var result = TestCaseImportParser.Parse(ExampleCsv);

        var registration = result.Scopes.Single(s => s.Name == "Registration");
        var @case = Assert.Single(registration.Cases);
        Assert.Equal("Open the app\nGo to the login page\nClick register\nenter an email that hasn't been used", @case.Steps);
        Assert.Equal(["D-1"], @case.DefectTokens);
    }

    [Fact]
    public void Dense_block_with_repeated_scope_and_scenario_is_one_case()
    {
        var result = TestCaseImportParser.Parse(ExampleCsv);

        var auth = result.Scopes.Single(s => s.Name == "Authentication");
        var @case = Assert.Single(auth.Cases);
        Assert.Equal("Open the app\nclick login\nrepeat 3x", @case.Steps);
        Assert.Equal(["D-2"], @case.DefectTokens);
    }

    [Fact]
    public void Step_less_cases_have_null_steps()
    {
        var result = TestCaseImportParser.Parse(ExampleCsv);

        Assert.Null(result.Scopes.Single(s => s.Name == "Storage").Cases.Single().Steps);
    }

    [Theory]
    [InlineData("Functional", TestCaseKind.Functional)]
    [InlineData("func", TestCaseKind.Functional)]
    [InlineData("Non-Functional", TestCaseKind.NonFunctional)]
    [InlineData("Non Functional", TestCaseKind.NonFunctional)]
    [InlineData("NonFunctional", TestCaseKind.NonFunctional)]
    [InlineData("  non-functional  ", TestCaseKind.NonFunctional)]
    [InlineData("NFR", TestCaseKind.NonFunctional)]
    public void Type_spellings_all_normalise(string spelling, TestCaseKind expected)
    {
        var csv = $"Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\nS,{spelling},A scenario,do a thing,\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.False(result.HasErrors);
        Assert.Equal(expected, result.Scopes.Single().Kind);
    }

    [Fact]
    public void Multiple_defect_links_across_rows_are_all_collected()
    {
        var csv =
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
            "S,Functional,A scenario,step one,D-1\n" +
            ",,,step two,D-2\n" +
            ",,,,D-3 D-4\n";

        var @case = TestCaseImportParser.Parse(csv).Scopes.Single().Cases.Single();

        Assert.Equal(["D-1", "D-2", "D-3", "D-4"], @case.DefectTokens);
    }

    [Fact]
    public void Missing_required_column_is_a_blocking_error()
    {
        var csv = "Scope,Scenario,Steps\nS,A,do\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.True(result.HasErrors);
        Assert.Contains("Test Case Type", result.Errors.Single().Message);
    }

    [Fact]
    public void Empty_file_is_a_blocking_error()
    {
        Assert.True(TestCaseImportParser.Parse(string.Empty).HasErrors);
    }

    [Fact]
    public void Missing_scenario_is_a_blocking_error()
    {
        var csv = "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\nS,Functional,,do a thing,\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.Contains(result.Errors, e => e.Message.Contains("scenario is missing"));
    }

    [Fact]
    public void Missing_type_for_a_new_scope_is_a_blocking_error()
    {
        var csv = "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\nS,,A scenario,do a thing,\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.Contains(result.Errors, e => e.Message.Contains("type is missing"));
    }

    [Fact]
    public void Unrecognised_type_is_a_blocking_error()
    {
        var csv = "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\nS,Regression,A scenario,do a thing,\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.Contains(result.Errors, e => e.Message.Contains("isn't Functional"));
    }

    [Fact]
    public void Scope_name_over_200_characters_is_a_blocking_error()
    {
        var csv = $"Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n{new string('x', 201)},Functional,A scenario,do,\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.Contains(result.Errors, e => e.Message.Contains("the limit is 200"));
    }

    [Fact]
    public void Interleaved_scope_is_a_blocking_error()
    {
        var csv =
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
            "A,Functional,First,do,\n" +
            "B,Functional,Second,do,\n" +
            "A,Functional,Third,do,\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.Contains(result.Errors, e => e.Message.Contains("\"A\" appears again"));
    }

    [Fact]
    public void Interleaved_scenario_within_a_scope_is_a_blocking_error()
    {
        var csv =
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
            "A,Functional,t1,do,\n" +
            "A,Functional,t2,do,\n" +
            "A,Functional,t1,do,\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.Contains(result.Errors, e => e.Message.Contains("\"t1\" appears again"));
    }

    [Fact]
    public void One_scope_with_two_types_is_a_blocking_error()
    {
        var csv =
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
            "A,Functional,t1,do,\n" +
            "A,Non-Functional,t2,do,\n";

        var result = TestCaseImportParser.Parse(csv);

        Assert.Contains(result.Errors, e => e.Message.Contains("two different types"));
    }

    [Fact]
    public void Blank_spacer_rows_are_ignored()
    {
        var csv =
            "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n" +
            "A,Functional,t1,step one,\n" +
            ",,,,\n" +
            ",,,step two,\n";

        var @case = TestCaseImportParser.Parse(csv).Scopes.Single().Cases.Single();

        Assert.Equal("step one\nstep two", @case.Steps);
    }
}
