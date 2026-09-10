namespace QaTracker.Web.TestCases;

/// <summary>
/// Turns an import CSV into a structured <see cref="ParsedImport"/> — no database access.
///
/// <para>
/// The file may repeat the scope / type / scenario on every row, or give them once and leave
/// continuation rows blank (adding more step lines / defect links), or mix the two. A row
/// starts a new test case when its scenario differs from the current one, or its scope
/// differs from the current scope; an otherwise-blank row continues the current case.
/// </para>
///
/// <para>
/// Blocking errors detected here are purely structural: missing / renamed columns, a missing
/// scope / scenario / type, an unrecognised type, a scope name over 200 characters, one scope
/// name given two different types, and interleaving — a scope or a (scope, scenario) that
/// reappears after the file has moved on to something else.
/// </para>
/// </summary>
public static class TestCaseImportParser
{
    private const int ScopeNameMaxLength = 200;

    public static IReadOnlyList<string> RequiredColumns { get; } = ["Test Case Scope", "Test Case Type", "Test Case Scenario"];

    public static ParsedImport Parse(string csv)
    {
        var rows = Csv.Parse(csv ?? string.Empty);
        if (rows.Count == 0)
        {
            return Fail("The file is empty.");
        }

        var header = new HeaderMap(rows[0]);
        var missing = RequiredColumns.Where(c => header.Resolve(Aliases(c)) < 0).ToList();
        if (missing.Count > 0)
        {
            return Fail($"The file is missing required column(s): {string.Join(", ", missing)}. " +
                        "Download the template to see the expected format.");
        }

        return new Run(rows, header).Execute();
    }

    /// <summary>Accepts Functional / func and Non-Functional / Non Functional / NonFunctional / NFR.</summary>
    public static bool TryParseKind(string raw, out TestCaseKind kind)
    {
        var normalized = new string((raw ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        switch (normalized)
        {
            case "functional":
            case "func":
            case "f":
                kind = TestCaseKind.Functional;
                return true;
            case "nonfunctional":
            case "nonfunc":
            case "nfr":
            case "nf":
                kind = TestCaseKind.NonFunctional;
                return true;
            default:
                kind = TestCaseKind.Functional;
                return false;
        }
    }

    private static ParsedImport Fail(string message) =>
        new([], [new ImportIssue(ImportSeverity.Error, message)]);

    private static string[] Aliases(string column) => column switch
    {
        "Test Case Scope" => ["test case scope", "scope"],
        "Test Case Type" => ["test case type", "type"],
        "Test Case Scenario" => ["test case scenario", "test case name", "scenario", "name"],
        _ => [column.ToLowerInvariant()],
    };

    /// <summary>One pass over the data rows, holding the running scope / case cursor.</summary>
    private sealed class Run
    {
        private readonly IReadOnlyList<string[]> rows;
        private readonly int scopeCol, typeCol, scenarioCol, stepsCol, defectCol;

        private readonly List<ImportIssue> errors = [];
        private readonly List<ScopeBuilder> scopes = [];
        private readonly HashSet<string> closedScopes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> closedCases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TestCaseKind> typeByScope = new(StringComparer.OrdinalIgnoreCase);

        private ScopeBuilder? scope;
        private CaseBuilder? @case;

        public Run(IReadOnlyList<string[]> rows, HeaderMap header)
        {
            this.rows = rows;
            scopeCol = header.Resolve(Aliases("Test Case Scope"));
            typeCol = header.Resolve(Aliases("Test Case Type"));
            scenarioCol = header.Resolve(Aliases("Test Case Scenario"));
            stepsCol = header.Resolve("steps", "step");
            defectCol = header.Resolve("defectlink", "defect link", "defect", "defects");
        }

        public ParsedImport Execute()
        {
            for (var r = 1; r < rows.Count; r++)
            {
                HandleRow(rows[r], r + 1);
            }

            FlushCase();

            var parsedScopes = scopes
                .Select(s => new ParsedScope(s.Name, s.Kind, s.Cases))
                .Where(s => s.Cases.Count > 0)
                .ToList();

            return new ParsedImport(parsedScopes, errors);
        }

        private void HandleRow(string[] row, int line)
        {
            var rawScope = Cell(row, scopeCol);
            var rawType = Cell(row, typeCol);
            var rawScenario = Cell(row, scenarioCol);
            var rawSteps = Cell(row, stepsCol);
            var rawDefects = Cell(row, defectCol);

            if ((rawScope + rawType + rawScenario + rawSteps + rawDefects).Length == 0)
            {
                return; // blank spacer row
            }

            var scenarioChanged = rawScenario.Length > 0
                && (@case is null || !Same(rawScenario, @case.Scenario));
            var scopeChanged = rawScope.Length > 0
                && (scope is null || !Same(rawScope, scope.Name));

            if (scenarioChanged || scopeChanged)
            {
                StartCase(rawScope, rawType, rawScenario, rawSteps, rawDefects, line);
            }
            else
            {
                Continue(rawType, rawSteps, rawDefects, line);
            }
        }

        private void Continue(string rawType, string rawSteps, string rawDefects, int line)
        {
            if (@case is null)
            {
                Error(line, "a step or defect link before any test case scope / scenario.");
                return;
            }

            if (rawType.Length > 0 && TryParseKind(rawType, out var contKind) && scope is not null && contKind != scope.Kind)
            {
                Error(line, $"scope \"{scope.Name}\" is {TestCaseDisplay.KindLabel(scope.Kind)} here but " +
                            $"{TestCaseDisplay.KindLabel(contKind)} elsewhere.");
            }

            Append(@case, rawSteps, rawDefects);
        }

        private void StartCase(string rawScope, string rawType, string rawScenario, string rawSteps, string rawDefects, int line)
        {
            FlushCase();

            var scopeName = rawScope.Length > 0 ? rawScope : scope?.Name;
            if (string.IsNullOrEmpty(scopeName))
            {
                Error(line, "test case scope is missing.");
                return;
            }

            if (scopeName.Length > ScopeNameMaxLength)
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Row {line}: scope name is {scopeName.Length} characters — the limit is {ScopeNameMaxLength}.", line, scopeName));
                return;
            }

            if (!EnsureScope(scopeName, rawType, line))
            {
                return;
            }

            if (rawScenario.Length == 0)
            {
                Error(line, "test case scenario is missing.", scope!.Name);
                return;
            }

            if (closedCases.Contains(CaseKey(scope!.Name, rawScenario)))
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Row {line}: test case \"{rawScenario}\" appears again after other cases — keep its rows together.",
                    line, scope.Name, rawScenario));
                return;
            }

            @case = new CaseBuilder(rawScenario, line);
            Append(@case, rawSteps, rawDefects);
        }

        /// <summary>Positions <see cref="scope"/> on the row's scope, creating it if new. Returns false on a blocking error.</summary>
        private bool EnsureScope(string scopeName, string rawType, int line)
        {
            if (scope is not null && Same(scopeName, scope.Name))
            {
                if (rawType.Length > 0 && TryParseKind(rawType, out var sameScopeKind) && sameScopeKind != scope.Kind)
                {
                    errors.Add(new(ImportSeverity.Error,
                        $"Row {line}: scope \"{scope.Name}\" is given two different types.", line, scope.Name));
                }

                return true;
            }

            if (closedScopes.Contains(scopeName))
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Row {line}: scope \"{scopeName}\" appears again after other scopes — keep every row for a scope together.",
                    line, scopeName));
                return false;
            }

            if (scope is not null)
            {
                closedScopes.Add(scope.Name);
            }

            scope = new ScopeBuilder(scopeName, ResolveKind(scopeName, rawType, line));
            scopes.Add(scope);
            return true;
        }

        private TestCaseKind ResolveKind(string scopeName, string rawType, int line)
        {
            if (rawType.Length == 0)
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Row {line}: test case type is missing for scope \"{scopeName}\".", line, scopeName));
                return TestCaseKind.Functional;
            }

            if (!TryParseKind(rawType, out var kind))
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Row {line}: test case type \"{rawType}\" isn't Functional or Non-Functional.", line, scopeName));
                return TestCaseKind.Functional;
            }

            if (typeByScope.TryGetValue(scopeName, out var seen) && seen != kind)
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Row {line}: scope \"{scopeName}\" is given two different types.", line, scopeName));
            }

            typeByScope[scopeName] = kind;
            return kind;
        }

        private void FlushCase()
        {
            if (scope is not null && @case is not null)
            {
                scope.Cases.Add(@case.ToParsed());
                closedCases.Add(CaseKey(scope.Name, @case.Scenario));
            }

            @case = null;
        }

        private static void Append(CaseBuilder @case, string steps, string defects)
        {
            if (steps.Length > 0)
            {
                @case.Steps.Add(steps);
            }

            foreach (var token in defects.Split(
                [',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                @case.DefectTokens.Add(token);
            }
        }

        private void Error(int line, string message, string? scopeName = null) =>
            errors.Add(new(ImportSeverity.Error, $"Row {line}: {message}", line, scopeName));

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string CaseKey(string scope, string scenario) =>
            scope.ToLowerInvariant() + "\n" + scenario.ToLowerInvariant();

        private static string Cell(string[] row, int index) =>
            index >= 0 && index < row.Length ? row[index].Trim() : string.Empty;
    }

    private sealed class HeaderMap
    {
        private readonly Dictionary<string, int> columns = new(StringComparer.Ordinal);

        public HeaderMap(string[] headerRow)
        {
            for (var i = 0; i < headerRow.Length; i++)
            {
                var key = string.Join(' ', headerRow[i].Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                columns.TryAdd(key, i);
            }
        }

        public int Resolve(params string[] names)
        {
            foreach (var name in names)
            {
                if (columns.TryGetValue(name, out var index))
                {
                    return index;
                }
            }

            return -1;
        }
    }

    private sealed class ScopeBuilder(string name, TestCaseKind kind)
    {
        public string Name { get; } = name;

        public TestCaseKind Kind { get; } = kind;

        public List<ParsedCase> Cases { get; } = [];
    }

    private sealed class CaseBuilder(string scenario, int firstRow)
    {
        public string Scenario { get; } = scenario;

        public int FirstRow { get; } = firstRow;

        public List<string> Steps { get; } = [];

        public List<string> DefectTokens { get; } = [];

        public ParsedCase ToParsed() => new(
            Scenario,
            Steps.Count > 0 ? string.Join('\n', Steps) : null,
            DefectTokens,
            FirstRow);
    }
}
