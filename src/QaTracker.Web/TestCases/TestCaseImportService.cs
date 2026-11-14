using QaTracker.Web.Defects;
using QaTracker.Web.Projects;

namespace QaTracker.Web.TestCases;

/// <summary>
/// Database-aware half of the CSV importer. <see cref="BuildPlanAsync"/> turns a
/// <see cref="ParsedImport"/> into a preview of what will be created / updated / linked plus
/// every warning to show; <see cref="ApplyAsync"/> commits it by orchestrating the existing
/// scope / case / defect services (re-validating first, since the database may have moved on
/// since the preview was rendered).
///
/// <para>Nothing here is transactional — matching the rest of the app — but every step is
/// idempotent, so re-running an import converges rather than duplicating.</para>
/// </summary>
public sealed class TestCaseImportService(
    TestScopeService scopes,
    TestCaseService cases,
    DefectService defects,
    ProjectService projects)
{
    public async Task<ImportPlan> BuildPlanAsync(Guid projectId, ParsedImport parsed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var existingScopes = await scopes.ListForProjectAsync(projectId, ct);
        var defectsByNumber = (await defects.ListForProjectAsync(projectId, ct)).ToDictionary(d => d.Number);
        var scopeByNumber = existingScopes.ToDictionary(s => s.Number);
        var caseByNumber = existingScopes.SelectMany(s => s.Cases.Select(c => (Scope: s, Case: c)))
            .ToDictionary(x => x.Case.Number);

        var issues = new List<ImportIssue>(parsed.Errors);
        issues.AddRange(parsed.Warnings);
        var file = new FileContents(parsed);
        var scopePlans = new List<ScopePlan>();
        var claimedScopes = new HashSet<Guid>();
        var claimedCases = new HashSet<Guid>();

        foreach (var scope in parsed.Scopes)
        {
            var (existing, renameBlocked) = MatchScope(scope, existingScopes, scopeByNumber, file, issues);
            if (existing is not null && !claimedScopes.Add(existing.Id))
            {
                // Two file scopes on one existing scope — unless the parser already said why
                // (the same ID or the same name twice).
                if (!file.ScopeIsDuplicated(scope))
                {
                    issues.Add(new(ImportSeverity.Error,
                        $"Two scopes in the file both match {Ref(existing)} \"{existing.Name}\" — each existing scope can only appear once.",
                        scope.FirstRow, scope.Name));
                }
                existing = null;
            }

            // The type: as given, else the existing scope's, else Functional (flagged).
            var kind = scope.Kind ?? existing?.Kind ?? TestCaseKind.Functional;
            var kindAssumed = scope.Kind is null && existing is null;
            if (kindAssumed)
            {
                issues.Add(new(ImportSeverity.Warning,
                    $"No test case type was given for the new scope \"{scope.Name}\", so it will be created as Functional. " +
                    "Add a type in the file if it should be Non-Functional.", scope.FirstRow, scope.Name));
            }

            var kindClash = existing is not null && scope.Kind is { } given && existing.Kind != given;
            if (kindClash)
            {
                issues.Add(new(ImportSeverity.Error,
                    $"{Ref(existing!)} \"{existing!.Name}\" is {TestCaseDisplay.KindLabel(existing.Kind)}, but the file says " +
                    $"{TestCaseDisplay.KindLabel(kind)}. Change the file or the scope so they agree.", scope.FirstRow, scope.Name));
            }

            // The scope's own summary goes before its cases' notes.
            var scopeNotesAt = issues.Count;

            // Compare each case against what's there now, so the preview (and the import) only
            // touch what actually differs.
            var casePlans = new List<CasePlan>();
            foreach (var @case in scope.Cases)
            {
                var (existingCase, from) = MatchCase(@case, scope, existing, existingScopes, caseByNumber, file, issues);
                if (existingCase is not null && !claimedCases.Add(existingCase.Id))
                {
                    if (!file.CaseIsDuplicated(scope, @case))
                    {
                        issues.Add(new(ImportSeverity.Error,
                            $"Two test cases in the file both match {Ref(existingCase)} \"{existingCase.Scenario}\" — each existing test case can only appear once.",
                            @case.FirstRow, scope.Name, @case.Scenario));
                    }
                    existingCase = null;
                    from = null;
                }

                var movedFrom = existingCase is not null && from is not null && from.Id != existing?.Id ? from.Name : null;
                if (movedFrom is not null)
                {
                    issues.Add(new(ImportSeverity.Warning,
                        $"{Ref(existingCase!)} \"{existingCase!.Scenario}\" will move from scope \"{movedFrom}\" to \"{scope.Name}\", " +
                        "keeping its result, comments, files and defect links. Check that's intended.",
                        @case.FirstRow, scope.Name, @case.Scenario));
                }

                var nameClash = CheckNameClash(@case, scope, existing, existingCase, movedFrom is not null, file, issues);

                var action = existingCase is null ? ImportAction.Create
                    : Differs(existingCase, @case) ? ImportAction.Update
                    : ImportAction.Unchanged;

                var plan = new CasePlan(@case.Scenario, @case.Steps, action, [],
                    existingCase?.Scenario, existingCase?.Steps)
                {
                    ExistingId = existingCase?.Id,
                    Number = existingCase?.Number,
                    MovedFrom = movedFrom,
                    FirstRow = @case.FirstRow,
                };

                if (action == ImportAction.Create)
                {
                    var steps = LineDiff.Lines(@case.Steps).Count;
                    issues.Add(new(ImportSeverity.Info,
                        $"New test case \"{@case.Scenario}\" will be added to \"{scope.Name}\"" +
                        (steps > 0 ? $" with {Count(steps, "step")}." : " (no steps)."),
                        @case.FirstRow, scope.Name, @case.Scenario));
                }
                if (plan.ScenarioChanged && !nameClash)
                {
                    issues.Add(new(ImportSeverity.Info,
                        $"{Ref(existingCase!)} \"{existingCase!.Scenario}\" will be renamed to \"{@case.Scenario}\".",
                        @case.FirstRow, scope.Name, @case.Scenario));
                }
                if (plan.StepsChanged)
                {
                    issues.Add(new(ImportSeverity.Warning,
                        $"{Ref(existingCase!)} \"{@case.Scenario}\" — its steps will be replaced with the file's; the changes are shown below.",
                        @case.FirstRow, scope.Name, @case.Scenario));
                }

                var links = new List<DefectLinkPlan>();
                foreach (var token in @case.DefectTokens)
                {
                    AppraiseLink(token, @case, scope.Name, existingCase, defectsByNumber, issues, links);
                }

                casePlans.Add(plan with { DefectLinks = links });
            }

            var scopePlan = new ScopePlan(scope.Name, kind, existing is not null, casePlans)
            {
                RenameBlocked = renameBlocked,
                ExistingId = existing?.Id,
                Number = existing?.Number,
                ExistingName = existing?.Name,
                KindAssumed = kindAssumed,
            };
            if (!kindClash)
            {
                issues.InsertRange(scopeNotesAt, DescribeScope(scopePlan)
                    .Select(m => new ImportIssue(ImportSeverity.Info, m, scope.FirstRow, scope.Name)));
            }

            scopePlans.Add(scopePlan);
        }

        return new ImportPlan(scopePlans, issues);
    }

    private static string Ref(TestScope scope) => TestCaseDisplay.ScopeRef(scope.Number);

    private static string Ref(TestCase @case) => TestCaseDisplay.CaseRef(@case.Number);

    /// <summary>The existing scope a file scope is about: by its ID when the file gives one (so it
    /// can be renamed), else by name — skipping a scope the file renames to something else by its
    /// ID. An ID that isn't in the project falls back to the name.</summary>
    /// <returns>The match, and whether renaming it to the file's name is blocked (an error says why).</returns>
    private static (TestScope? Scope, bool RenameBlocked) MatchScope(
        ParsedScope scope, IReadOnlyList<TestScope> existingScopes, IReadOnlyDictionary<int, TestScope> byNumber,
        FileContents file, List<ImportIssue> issues)
    {
        var byName = existingScopes.FirstOrDefault(s => Same(s.Name, scope.Name) && !file.RenamesAway(s));
        if (scope.Number is not { } number)
        {
            return (byName, false);
        }

        if (!byNumber.TryGetValue(number, out var byId))
        {
            issues.Add(new(ImportSeverity.Warning,
                $"Scope ID {TestCaseDisplay.ScopeRef(number)} isn't in this project, so \"{scope.Name}\" will be " +
                (byName is null
                    ? "created as a new scope with a new ID."
                    : $"matched by name to {Ref(byName)} instead."),
                scope.FirstRow, scope.Name));
            return (byName, false);
        }

        var blocked = byName is not null && byName.Id != byId.Id;
        if (blocked)
        {
            issues.Add(new(ImportSeverity.Error,
                $"{Ref(byId)} \"{byId.Name}\" can't be renamed to \"{scope.Name}\" — {Ref(byName!)} already has that name " +
                "(rename that one too, or choose another name).", scope.FirstRow, scope.Name));
        }

        return (byId, blocked);
    }

    /// <summary>The existing case a file case is about, and the scope it's in now: by its ID
    /// anywhere in the project (so it can be renamed or moved), else by name in the matched scope.
    /// A name that only matches a case in a different scope is flagged — likely a slip.</summary>
    private static (TestCase? Case, TestScope? Scope) MatchCase(
        ParsedCase @case, ParsedScope scope, TestScope? target, IReadOnlyList<TestScope> existingScopes,
        IReadOnlyDictionary<int, (TestScope Scope, TestCase Case)> byNumber, FileContents file, List<ImportIssue> issues)
    {
        if (@case.Number is { } number && byNumber.TryGetValue(number, out var hit))
        {
            return (hit.Case, hit.Scope);
        }

        // By name — but not a case the file renames or moves elsewhere by its ID.
        var byName = target?.Cases.FirstOrDefault(c => Same(c.Scenario, @case.Scenario) && !file.Redirects(target, c));

        if (@case.Number is { } unknown)
        {
            issues.Add(new(ImportSeverity.Warning,
                $"Test case ID {TestCaseDisplay.CaseRef(unknown)} isn't in this project, so \"{@case.Scenario}\" will be " +
                (byName is null ? "added as a new test case with a new ID." : $"matched by name to {Ref(byName)} instead."),
                @case.FirstRow, scope.Name, @case.Scenario));
        }

        if (byName is not null)
        {
            return (byName, target);
        }

        // A same-named case in another scope — unless the file covers that one too (then the file's
        // own "is also in" warning already says so).
        var elsewhere = existingScopes
            .Where(s => s.Id != target?.Id)
            .SelectMany(s => s.Cases.Where(c => Same(c.Scenario, @case.Scenario)).Select(c => (Scope: s, Case: c)))
            .FirstOrDefault(x => !file.Covers(x.Scope, x.Case));
        if (elsewhere.Case is not null)
        {
            issues.Add(new(ImportSeverity.Warning,
                $"\"{@case.Scenario}\" already exists in scope \"{elsewhere.Scope.Name}\" ({Ref(elsewhere.Case)}), so importing will add " +
                $"a second, separate copy to \"{scope.Name}\". Check the file — to move {Ref(elsewhere.Case)} instead, give this row its ID.",
                @case.FirstRow, scope.Name, @case.Scenario));
        }

        return (null, null);
    }

    /// <summary>What the file says about existing scopes and cases, for matching: which IDs it
    /// renames or moves, which existing cases it already accounts for, and which names / IDs it
    /// repeats (the parser has already reported those).</summary>
    private sealed class FileContents
    {
        private readonly ParsedImport parsed;
        private readonly Dictionary<int, string> scopeNameByNumber = [];
        private readonly Dictionary<int, (ParsedScope Scope, ParsedCase Case)> caseByNumber = [];
        private readonly Dictionary<int, int> caseNumberCount = [];

        public FileContents(ParsedImport parsed)
        {
            this.parsed = parsed;
            foreach (var s in parsed.Scopes)
            {
                if (s.Number is { } sn)
                {
                    scopeNameByNumber.TryAdd(sn, s.Name);
                }

                foreach (var c in s.Cases.Where(c => c.Number is not null))
                {
                    caseByNumber.TryAdd(c.Number!.Value, (s, c));
                    caseNumberCount[c.Number!.Value] = caseNumberCount.GetValueOrDefault(c.Number!.Value) + 1;
                }
            }
        }

        /// <summary>The file gives this scope's ID with a different name.</summary>
        public bool RenamesAway(TestScope scope) =>
            scopeNameByNumber.TryGetValue(scope.Number, out var name) && !Same(name, scope.Name);

        /// <summary>The file gives this case's ID under another name or another scope.</summary>
        public bool Redirects(TestScope scope, TestCase @case) =>
            caseByNumber.TryGetValue(@case.Number, out var x)
            && (!Same(x.Case.Scenario, @case.Scenario) || !IsScope(x.Scope, scope));

        /// <summary>The file mentions this existing case (by ID, or by name in its scope).</summary>
        public bool Covers(TestScope scope, TestCase @case) =>
            caseByNumber.ContainsKey(@case.Number)
            || parsed.Scopes.Any(s => IsScope(s, scope) && s.Cases.Any(c => Same(c.Scenario, @case.Scenario)));

        public bool ScopeIsDuplicated(ParsedScope scope) =>
            parsed.Scopes.Count(s => Same(s.Name, scope.Name) || (scope.Number is not null && s.Number == scope.Number)) > 1;

        public bool CaseIsDuplicated(ParsedScope scope, ParsedCase @case) =>
            (@case.Number is { } n && caseNumberCount.GetValueOrDefault(n) > 1)
            || scope.Cases.Count(c => Same(c.Scenario, @case.Scenario)) > 1;

        private static bool IsScope(ParsedScope fileScope, TestScope scope) =>
            fileScope.Number is { } n ? n == scope.Number : Same(fileScope.Name, scope.Name);
    }

    /// <summary>Renaming a case (matched by ID) to the name of another case already in the target
    /// scope, or moving it into a scope that already has a case by that name, would leave two
    /// cases with one name — an error. A case the file itself renames or moves away doesn't count.</summary>
    private static bool CheckNameClash(
        ParsedCase @case, ParsedScope scope, TestScope? target, TestCase? existingCase, bool moving, FileContents file,
        List<ImportIssue> issues)
    {
        if (existingCase is null || target is null || (!moving && Same(existingCase.Scenario, @case.Scenario)))
        {
            return false;
        }

        var clash = target.Cases.FirstOrDefault(c =>
            c.Id != existingCase.Id && Same(c.Scenario, @case.Scenario) && !file.Redirects(target, c));
        if (clash is not null)
        {
            issues.Add(new(ImportSeverity.Error,
                $"{Ref(existingCase)} can't be {(moving ? "moved into" : "renamed in")} \"{target.Name}\" as \"{@case.Scenario}\" — " +
                $"{Ref(clash)} there already has that name.", @case.FirstRow, scope.Name, @case.Scenario));
        }

        return clash is not null;
    }

    public async Task<ImportResult> ApplyAsync(Guid projectId, ParsedImport parsed, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        if (parsed.HasErrors)
        {
            return ImportResult.Blocked(parsed.Errors);
        }

        // Built fresh — the database may have moved on since the preview — and carried out as planned.
        var plan = await BuildPlanAsync(projectId, parsed, ct);
        if (!plan.CanImport)
        {
            return ImportResult.Blocked(plan.Errors.ToList());
        }

        var defectsByNumber = (await defects.ListForProjectAsync(projectId, ct)).ToDictionary(d => d.Number);
        int scopesCreated = 0, scopesRenamed = 0, casesCreated = 0, casesUpdated = 0, casesMoved = 0, defectsLinked = 0;

        foreach (var scope in plan.Scopes)
        {
            Guid scopeId;
            if (scope.ExistingId is { } existingScopeId)
            {
                scopeId = existingScopeId;
                if (scope.Renamed)
                {
                    await scopes.UpdateAsync(scopeId, scope.Kind, scope.Name, userId, ct);
                    scopesRenamed++;
                }
            }
            else
            {
                scopeId = (await scopes.CreateAsync(projectId, scope.Kind, scope.Name, userId, ct)).Id;
                scopesCreated++;
            }

            foreach (var @case in scope.Cases)
            {
                var input = new TestCaseInput(@case.Scenario, @case.Steps);
                Guid caseId;
                if (@case.ExistingId is { } existingCaseId)
                {
                    caseId = existingCaseId;
                    if (@case.MovedFrom is not null)
                    {
                        await cases.MoveAsync(caseId, scopeId, userId, ct);
                        casesMoved++;
                    }

                    // Leave a case the file already matches alone — no rewrite, no history entry.
                    if (@case.Action == ImportAction.Update)
                    {
                        await cases.UpdateAsync(caseId, input, userId, ct);
                        casesUpdated++;
                    }
                }
                else
                {
                    caseId = (await cases.CreateAsync(scopeId, input, userId, imported: true, ct)).Id;
                    casesCreated++;
                }

                foreach (var link in @case.NewLinks)
                {
                    if (defectsByNumber.TryGetValue(link.Number, out var defect) && defect.TestCases.All(tc => tc.Id != caseId))
                    {
                        await defects.LinkTestCaseAsync(defect.Id, caseId, userId, ct);
                        defectsLinked++;
                    }
                }
            }
        }

        if (DescribeImport(scopesCreated, casesCreated, casesUpdated, defectsLinked, scopesRenamed, casesMoved) is { } summary)
        {
            await projects.RecordAsync(projectId, ProjectEventKind.TestCasesImported, userId, toValue: summary, ct: ct);
        }

        return new ImportResult(true, scopesCreated, casesCreated, casesUpdated, defectsLinked,
            plan.Issues.Where(i => i.Severity != ImportSeverity.Error).ToList())
        {
            ScopesRenamed = scopesRenamed,
            CasesMoved = casesMoved,
        };
    }

    /// <summary>"12 new test cases, 3 updated, 2 new scopes" — what the project's activity says
    /// an import did; null when it changed nothing.</summary>
    public static string? DescribeImport(
        int scopesCreated, int casesCreated, int casesUpdated, int defectsLinked, int scopesRenamed = 0, int casesMoved = 0)
    {
        var parts = new List<string>();
        if (casesCreated > 0) parts.Add(Count(casesCreated, "new test case"));
        if (casesUpdated > 0) parts.Add($"{casesUpdated} updated");
        if (casesMoved > 0) parts.Add($"{casesMoved} moved");
        if (scopesCreated > 0) parts.Add(Count(scopesCreated, "new scope"));
        if (scopesRenamed > 0) parts.Add($"{Count(scopesRenamed, "scope")} renamed");
        if (defectsLinked > 0) parts.Add(Count(defectsLinked, "new defect link"));
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static void AppraiseLink(
        string token,
        ParsedCase @case,
        string scopeName,
        TestCase? existingCase,
        IReadOnlyDictionary<int, Defect> defectsByNumber,
        List<ImportIssue> issues,
        List<DefectLinkPlan> links)
    {
        if (!TryParseDefectNumber(token, out var number))
        {
            issues.Add(new(ImportSeverity.Warning,
                $"\"{token}\" in the defect link column for \"{@case.Scenario}\" isn't a defect reference (expected e.g. D-3), so it will be skipped.",
                @case.FirstRow, scopeName, @case.Scenario));
            return;
        }

        if (!defectsByNumber.TryGetValue(number, out var defect))
        {
            issues.Add(new(ImportSeverity.Warning,
                $"{DefectDisplay.Ref(number)} doesn't exist in this project, so the link from \"{@case.Scenario}\" will be skipped.",
                @case.FirstRow, scopeName, @case.Scenario));
            return;
        }

        // Already linked: nothing will change, so nothing to say about it.
        var alreadyLinked = existingCase is not null && defect.TestCases.Any(tc => tc.Id == existingCase.Id);
        if (alreadyLinked)
        {
            links.Add(new DefectLinkPlan(number, AlreadyLinked: true));
            return;
        }

        if (defect.TestCases.Count > 0)
        {
            var other = defect.TestCases[0].Scenario;
            var more = defect.TestCases.Count > 1 ? $" and {defect.TestCases.Count - 1} more" : string.Empty;
            issues.Add(new(ImportSeverity.Warning,
                $"{DefectDisplay.Ref(number)} is already linked to \"{other}\"{more}; importing will link it to \"{@case.Scenario}\" as well.",
                @case.FirstRow, scopeName, @case.Scenario));
        }
        else
        {
            issues.Add(new(ImportSeverity.Info,
                $"{DefectDisplay.Ref(number)} will be linked to \"{@case.Scenario}\".",
                @case.FirstRow, scopeName, @case.Scenario));
        }

        links.Add(new DefectLinkPlan(number, AlreadyLinked: false));
    }

    /// <summary>Accepts "D-3", "d3", "#3", " 3 ".</summary>
    internal static bool TryParseDefectNumber(string token, out int number)
    {
        number = 0;
        var trimmed = (token ?? string.Empty).Trim().TrimStart('#');
        if (trimmed.StartsWith("D-", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith('D') || trimmed.StartsWith('d'))
        {
            trimmed = trimmed[1..];
        }

        return int.TryParse(trimmed.Trim(), out number) && number > 0;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether importing would change an existing case: its scenario wording (cases
    /// match case-insensitively, so "login works" → "Login works" is a change) or its steps,
    /// ignoring line endings, surrounding whitespace and blank lines.</summary>
    internal static bool Differs(TestCase existing, ParsedCase incoming) =>
        !string.Equals(existing.Scenario, incoming.Scenario.Trim(), StringComparison.Ordinal)
        || !LineDiff.Lines(existing.Steps).SequenceEqual(LineDiff.Lines(incoming.Steps));

    /// <summary>The scope's "what will happen" lines — always what the import will do, or that it
    /// will leave the scope alone.</summary>
    private static IEnumerable<string> DescribeScope(ScopePlan scope)
    {
        var created = scope.Cases.Count(c => c.Action == ImportAction.Create);
        var updated = scope.Cases.Count(c => c.Action == ImportAction.Update);
        var moved = scope.Cases.Count(c => c.MovedFrom is not null);
        var links = scope.Cases.Sum(c => c.NewLinks.Count);
        var kind = TestCaseDisplay.KindLabel(scope.Kind);

        if (!scope.Exists)
        {
            var with = new List<string>();
            if (created > 0) with.Add(Count(created, "new test case"));
            if (moved > 0) with.Add($"{Count(moved, "test case")} moved in from other scopes");
            yield return $"New {kind} scope \"{scope.Name}\" will be created" +
                (with.Count > 0 ? $" with {string.Join(" and ", with)}." : ".");
            yield break;
        }

        var reference = scope.Number is { } n ? TestCaseDisplay.ScopeRef(n) : "Scope";
        var renaming = scope.Renamed && !scope.RenameBlocked;
        if (renaming)
        {
            yield return $"{reference} \"{scope.ExistingName}\" will be renamed to \"{scope.Name}\".";
        }

        var changes = new List<string>();
        if (created > 0) changes.Add($"{Count(created, "new test case")} will be added");
        if (updated > 0) changes.Add($"{Count(updated, "test case")} will be updated");
        if (moved > 0) changes.Add($"{Count(moved, "test case")} will be moved in from other scopes");
        if (links > 0) changes.Add($"{Count(links, "defect link")} will be added");

        var name = renaming || !scope.Renamed ? scope.Name : scope.ExistingName;
        if (changes.Count > 0)
        {
            yield return $"In {reference} \"{name}\": {string.Join(", ", changes)}" +
                (scope.UnchangedCount > 0 ? $"; {scope.UnchangedCount} unchanged." : ".");
        }
        else if (renaming)
        {
            yield return $"Nothing else in {reference} will change ({Count(scope.Cases.Count, "test case")} checked).";
        }
        else
        {
            yield return $"{reference} \"{name}\" already matches the file — nothing in it will change " +
                $"({Count(scope.Cases.Count, "test case")} checked).";
        }
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
}
