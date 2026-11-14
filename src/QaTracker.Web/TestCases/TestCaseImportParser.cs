namespace QaTracker.Web.TestCases;

/// <summary>
/// Turns an import CSV into a structured <see cref="ParsedImport"/> — no database access.
///
/// <para>
/// The file may repeat the scope / type / scenario on every row, or give them once and leave
/// continuation rows blank (adding more step lines / defect links), or mix the two. A row
/// starts a new test case when its scenario, test case ID or scope differs from the current
/// one; an otherwise-blank row continues the current case. The "Scope ID" and "Test Case ID"
/// columns are optional — files exported from QA Tracker carry them, so matching can go by ID.
/// </para>
///
/// <para>
/// Blocking errors detected here are purely structural: missing / renamed columns, a missing
/// scope or scenario, an unrecognised type or ID, a scope name over 200 characters, one scope
/// name given two different types, a scope that reappears after the file has moved on, two
/// cases with the same name in one scope, and an ID used twice. A missing type isn't an error
/// (the scope's existing type, or Functional, is used — the plan says which); the same scenario
/// in two scopes is a warning.
/// </para>
/// </summary>
public static class TestCaseImportParser
{
    private const int ScopeNameMaxLength = 200;

    public static IReadOnlyList<string> RequiredColumns { get; } = ["Test Case Scope", "Test Case Scenario"];

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
        "Scope ID" => ["scope id", "scopeid", "scope ref"],
        "Test Case ID" => ["test case id", "testcase id", "case id", "caseid", "id"],
        _ => [column.ToLowerInvariant()],
    };

    /// <summary>One pass over the data rows, holding the running scope / case cursor.</summary>
    private sealed class Run
    {
        private readonly IReadOnlyList<string[]> rows;
        private readonly int scopeCol, typeCol, scenarioCol, stepsCol, defectCol, scopeIdCol, caseIdCol;

        private readonly List<ImportIssue> errors = [];
        private readonly List<ImportIssue> warnings = [];
        private readonly List<ScopeBuilder> scopes = [];
        private readonly HashSet<string> closedScopes = new(StringComparer.OrdinalIgnoreCase);

        private ScopeBuilder? scope;
        private CaseBuilder? @case;

        public Run(IReadOnlyList<string[]> rows, HeaderMap header)
        {
            this.rows = rows;
            scopeCol = header.Resolve(Aliases("Test Case Scope"));
            typeCol = header.Resolve(Aliases("Test Case Type"));
            scenarioCol = header.Resolve(Aliases("Test Case Scenario"));
            scopeIdCol = header.Resolve(Aliases("Scope ID"));
            caseIdCol = header.Resolve(Aliases("Test Case ID"));
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
                .Where(s => s.Cases.Count > 0)
                .Select(s => new ParsedScope(s.Name, s.Kind, s.Cases, s.FirstRow, s.Number))
                .ToList();

            CheckDuplicates(parsedScopes);
            return new ParsedImport(parsedScopes, errors) { Warnings = warnings };
        }

        private void HandleRow(string[] row, int line)
        {
            var rawScope = Cell(row, scopeCol);
            var rawType = Cell(row, typeCol);
            var rawScenario = Cell(row, scenarioCol);
            var rawSteps = Cell(row, stepsCol);
            var rawDefects = Cell(row, defectCol);
            var rawScopeId = Cell(row, scopeIdCol);
            var rawCaseId = Cell(row, caseIdCol);

            if ((rawScope + rawType + rawScenario + rawSteps + rawDefects + rawScopeId + rawCaseId).Length == 0)
            {
                return; // blank spacer row
            }

            var scopeId = ReadId(rawScopeId, line, "scope", TestCaseDisplay.TryParseScopeRef, "S-3");
            var caseId = ReadId(rawCaseId, line, "test case", TestCaseDisplay.TryParseCaseRef, "T-12");

            var scenarioChanged = rawScenario.Length > 0
                && (@case is null || !Same(rawScenario, @case.Scenario));
            var caseIdChanged = caseId is not null && @case?.Number != caseId;
            var scopeChanged = (rawScope.Length > 0 && (scope is null || !Same(rawScope, scope.Name)))
                || (scopeId is not null && scope?.Number is { } current && current != scopeId);

            if (scenarioChanged || caseIdChanged || scopeChanged)
            {
                StartCase(rawScope, rawType, rawScenario, rawSteps, rawDefects, scopeId, caseId, line);
            }
            else
            {
                Continue(rawType, rawSteps, rawDefects, line);
            }
        }

        private int? ReadId(string raw, int line, string what, TryParse parse, string example)
        {
            if (raw.Length == 0)
            {
                return null;
            }

            if (parse(raw, out var number))
            {
                return number;
            }

            Error(line, $"\"{raw}\" isn't a valid {what} ID (expected e.g. {example}).");
            return null;
        }

        private delegate bool TryParse(string? raw, out int number);

        private void Continue(string rawType, string rawSteps, string rawDefects, int line)
        {
            if (@case is null)
            {
                Error(line, "a step or defect link before any test case scope / scenario.");
                return;
            }

            if (rawType.Length > 0 && scope is not null)
            {
                ApplyType(scope, rawType, line);
            }

            Append(@case, rawSteps, rawDefects);
        }

        private void StartCase(
            string rawScope, string rawType, string rawScenario, string rawSteps, string rawDefects,
            int? scopeId, int? caseId, int line)
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
                    $"Scope name is {scopeName.Length} characters — the limit is {ScopeNameMaxLength}.", line, scopeName));
                return;
            }

            if (!EnsureScope(scopeName, rawType, scopeId, line))
            {
                return;
            }

            if (rawScenario.Length == 0)
            {
                Error(line, "test case scenario is missing.", scope!.Name);
                return;
            }

            @case = new CaseBuilder(rawScenario, line, caseId);
            Append(@case, rawSteps, rawDefects);
        }

        /// <summary>Positions <see cref="scope"/> on the row's scope, creating it if new. Returns false on a blocking error.</summary>
        private bool EnsureScope(string scopeName, string rawType, int? scopeId, int line)
        {
            if (scope is not null && Same(scopeName, scope.Name) && (scopeId is null || scope.Number is null || scope.Number == scopeId))
            {
                scope.Number ??= scopeId;
                if (rawType.Length > 0)
                {
                    ApplyType(scope, rawType, line);
                }

                return true;
            }

            if (closedScopes.Contains(scopeName))
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Scope \"{scopeName}\" appears again after other scopes — keep every row for a scope together.",
                    line, scopeName));
                return false;
            }

            if (scope is not null)
            {
                closedScopes.Add(scope.Name);
            }

            scope = new ScopeBuilder(scopeName, line, scopeId);
            if (rawType.Length > 0)
            {
                ApplyType(scope, rawType, line);
            }

            scopes.Add(scope);
            return true;
        }

        /// <summary>Records the scope's type from a row; an unknown type or a second, different
        /// type is an error. No type at all is fine (see <see cref="ParsedScope.Kind"/>).</summary>
        private void ApplyType(ScopeBuilder target, string rawType, int line)
        {
            if (!TryParseKind(rawType, out var kind))
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Test case type \"{rawType}\" isn't Functional or Non-Functional.", line, target.Name));
                return;
            }

            if (target.Kind is { } seen && seen != kind)
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Scope \"{target.Name}\" is given two different types.", line, target.Name));
                return;
            }

            target.Kind = kind;
        }

        /// <summary>Duplicate names within a scope and IDs used twice are errors; the same scenario in
        /// two different scopes is probably a slip in the file, so it's a warning.</summary>
        private void CheckDuplicates(IReadOnlyList<ParsedScope> parsed)
        {
            foreach (var s in parsed)
            {
                foreach (var dup in s.Cases.GroupBy(c => c.Scenario, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                {
                    var rowsText = string.Join(", ", dup.Select(c => c.FirstRow));
                    errors.Add(new(ImportSeverity.Error,
                        $"Scope \"{s.Name}\" has more than one test case called \"{dup.First().Scenario}\" (rows {rowsText}). " +
                        "Names must be unique within a scope — rename one, or keep a case's rows together.",
                        dup.First().FirstRow, s.Name, dup.First().Scenario));
                }
            }

            // Two scopes can't share a name — e.g. two IDs both renamed to the same thing.
            foreach (var dup in parsed.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                var which = string.Join(", ", dup.Select(s => s.Number is { } n ? $"{TestCaseDisplay.ScopeRef(n)} (row {s.FirstRow})" : $"row {s.FirstRow}"));
                errors.Add(new(ImportSeverity.Error,
                    $"More than one scope in the file is called \"{dup.First().Name}\" ({which}). Scope names must be unique — " +
                    "give each its own name, or put their test cases under one scope.", dup.First().FirstRow, dup.First().Name));
            }

            foreach (var dup in parsed.Where(s => s.Number is not null).GroupBy(s => s.Number!.Value).Where(g => g.Count() > 1))
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Scope ID {TestCaseDisplay.ScopeRef(dup.Key)} is used for more than one scope " +
                    $"({string.Join(", ", dup.Select(s => $"\"{s.Name}\""))}).", dup.First().FirstRow, dup.First().Name));
            }

            var allCases = parsed.SelectMany(s => s.Cases.Select(c => (Scope: s, Case: c))).ToList();
            foreach (var dup in allCases.Where(x => x.Case.Number is not null).GroupBy(x => x.Case.Number!.Value).Where(g => g.Count() > 1))
            {
                errors.Add(new(ImportSeverity.Error,
                    $"Test case ID {TestCaseDisplay.CaseRef(dup.Key)} is used for more than one test case " +
                    $"(rows {string.Join(", ", dup.Select(x => x.Case.FirstRow))}).",
                    dup.First().Case.FirstRow, dup.First().Scope.Name, dup.First().Case.Scenario));
            }

            // Flagged on each copy, so every row it's on says so.
            foreach (var dup in allCases.GroupBy(x => x.Case.Scenario, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Select(x => x.Scope.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            {
                foreach (var x in dup)
                {
                    var others = string.Join(" and ", dup.Where(o => !ReferenceEquals(o.Case, x.Case))
                        .Select(o => $"\"{o.Scope.Name}\" (row {o.Case.FirstRow})"));
                    warnings.Add(new(ImportSeverity.Warning,
                        $"\"{x.Case.Scenario}\" in \"{x.Scope.Name}\" is also in {others}. " +
                        "Check the file: if it's the same test, it should only be in one scope.",
                        x.Case.FirstRow, x.Scope.Name, x.Case.Scenario));
                }
            }
        }

        private void FlushCase()
        {
            if (scope is not null && @case is not null)
            {
                scope.Cases.Add(@case.ToParsed());
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
            // The row is shown beside the message (ImportIssue.Row), so it isn't repeated in the text.
            errors.Add(new(ImportSeverity.Error, char.ToUpperInvariant(message[0]) + message[1..], line, scopeName));

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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

    private sealed class ScopeBuilder(string name, int firstRow, int? number)
    {
        public string Name { get; } = name;

        public int FirstRow { get; } = firstRow;

        public int? Number { get; set; } = number;

        public TestCaseKind? Kind { get; set; }

        public List<ParsedCase> Cases { get; } = [];
    }

    private sealed class CaseBuilder(string scenario, int firstRow, int? number)
    {
        public string Scenario { get; } = scenario;

        public int FirstRow { get; } = firstRow;

        public int? Number { get; } = number;

        public List<string> Steps { get; } = [];

        public List<string> DefectTokens { get; } = [];

        public ParsedCase ToParsed() => new(
            Scenario,
            Steps.Count > 0 ? string.Join('\n', Steps) : null,
            DefectTokens,
            FirstRow,
            Number);
    }
}
