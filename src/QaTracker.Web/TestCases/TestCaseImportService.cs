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

        var issues = new List<ImportIssue>(parsed.Errors);
        var scopePlans = new List<ScopePlan>();

        foreach (var scope in parsed.Scopes)
        {
            var existing = existingScopes.FirstOrDefault(s => Same(s.Name, scope.Name));
            var scopeLabel = TestCaseDisplay.KindLabel(scope.Kind);

            var kindClash = existing is not null && existing.Kind != scope.Kind;
            if (kindClash)
            {
                issues.Add(new(ImportSeverity.Error,
                    $"Scope \"{scope.Name}\" already exists as {TestCaseDisplay.KindLabel(existing!.Kind)}, but the file says {scopeLabel}. " +
                    "Change the file or the scope so they agree.", Scope: scope.Name));
            }

            // Compare each case against what's there now, so the preview (and the import) only
            // touch what actually differs.
            var casePlans = new List<CasePlan>();
            foreach (var @case in scope.Cases)
            {
                var existingCase = existing?.Cases.FirstOrDefault(c => Same(c.Scenario, @case.Scenario));
                var action = existingCase is null ? ImportAction.Create
                    : Differs(existingCase, @case) ? ImportAction.Update
                    : ImportAction.Unchanged;

                var plan = new CasePlan(@case.Scenario, @case.Steps, action, [],
                    existingCase?.Scenario, existingCase?.Steps);
                if (plan.StepsChanged)
                {
                    issues.Add(new(ImportSeverity.Warning,
                        $"Test case \"{@case.Scenario}\" in \"{scope.Name}\" — its steps will be replaced (changes shown below).",
                        @case.FirstRow, scope.Name, @case.Scenario));
                }

                var links = new List<DefectLinkPlan>();
                foreach (var token in @case.DefectTokens)
                {
                    AppraiseLink(token, @case, scope.Name, existingCase, defectsByNumber, issues, links);
                }

                casePlans.Add(plan with { DefectLinks = links });
            }

            var scopePlan = new ScopePlan(scope.Name, scope.Kind, existing is not null, casePlans);
            if (!kindClash)
            {
                issues.Add(new(ImportSeverity.Info, DescribeScope(scopePlan, scopeLabel), Scope: scope.Name));
            }

            scopePlans.Add(scopePlan);
        }

        return new ImportPlan(scopePlans, issues);
    }

    public async Task<ImportResult> ApplyAsync(Guid projectId, ParsedImport parsed, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        if (parsed.HasErrors)
        {
            return ImportResult.Blocked(parsed.Errors);
        }

        var plan = await BuildPlanAsync(projectId, parsed, ct);
        if (!plan.CanImport)
        {
            return ImportResult.Blocked(plan.Errors.ToList());
        }

        var existingScopes = (await scopes.ListForProjectAsync(projectId, ct)).ToList();
        var defectsByNumber = (await defects.ListForProjectAsync(projectId, ct)).ToDictionary(d => d.Number);

        int scopesCreated = 0, casesCreated = 0, casesUpdated = 0, defectsLinked = 0;

        foreach (var scope in parsed.Scopes)
        {
            var existing = existingScopes.FirstOrDefault(s => Same(s.Name, scope.Name));
            Guid scopeId;
            List<TestCase> scopeCases;

            if (existing is null)
            {
                var created = await scopes.CreateAsync(projectId, scope.Kind, scope.Name, userId, ct);
                scopeId = created.Id;
                scopeCases = [];
                scopesCreated++;
            }
            else
            {
                scopeId = existing.Id;
                scopeCases = existing.Cases.ToList();
            }

            foreach (var @case in scope.Cases)
            {
                var input = new TestCaseInput(@case.Scenario, @case.Steps);
                var existingCase = scopeCases.FirstOrDefault(c => Same(c.Scenario, @case.Scenario));

                Guid caseId;
                if (existingCase is null)
                {
                    var created = await cases.CreateAsync(scopeId, input, userId, imported: true, ct);
                    caseId = created.Id;
                    casesCreated++;
                }
                else
                {
                    // Leave a case the file already matches alone — no rewrite, no history entry.
                    if (Differs(existingCase, @case))
                    {
                        await cases.UpdateAsync(existingCase.Id, input, userId, ct);
                        casesUpdated++;
                    }
                    caseId = existingCase.Id;
                }

                foreach (var token in @case.DefectTokens)
                {
                    if (TryParseDefectNumber(token, out var number)
                        && defectsByNumber.TryGetValue(number, out var defect))
                    {
                        var wasLinked = defect.TestCases.Any(tc => tc.Id == caseId);
                        await defects.LinkTestCaseAsync(defect.Id, caseId, userId, ct);
                        if (!wasLinked)
                        {
                            defectsLinked++;
                        }
                    }
                }
            }
        }

        if (DescribeImport(scopesCreated, casesCreated, casesUpdated, defectsLinked) is { } summary)
        {
            await projects.RecordAsync(projectId, ProjectEventKind.TestCasesImported, userId, toValue: summary, ct: ct);
        }

        return new ImportResult(true, scopesCreated, casesCreated, casesUpdated, defectsLinked,
            plan.Issues.Where(i => i.Severity != ImportSeverity.Error).ToList());
    }

    /// <summary>"12 new test cases, 3 updated, 2 new scopes" — what the project's activity says
    /// an import did; null when it changed nothing.</summary>
    public static string? DescribeImport(int scopesCreated, int casesCreated, int casesUpdated, int defectsLinked)
    {
        var parts = new List<string>();
        if (casesCreated > 0) parts.Add(Count(casesCreated, "new test case"));
        if (casesUpdated > 0) parts.Add($"{casesUpdated} updated");
        if (scopesCreated > 0) parts.Add(Count(scopesCreated, "new scope"));
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
                $"Defect link \"{token}\" on \"{@case.Scenario}\" isn't a valid reference (expected e.g. D-3) — it will be skipped.",
                @case.FirstRow, scopeName, @case.Scenario));
            return;
        }

        if (!defectsByNumber.TryGetValue(number, out var defect))
        {
            issues.Add(new(ImportSeverity.Warning,
                $"Defect {DefectDisplay.Ref(number)} linked from \"{@case.Scenario}\" doesn't exist in this project — it will be skipped.",
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
                $"{DefectDisplay.Ref(number)} is currently linked to \"{other}\"{more}. Importing will also link it to \"{@case.Scenario}\".",
                @case.FirstRow, scopeName, @case.Scenario));
        }
        else
        {
            issues.Add(new(ImportSeverity.Info,
                $"{DefectDisplay.Ref(number)} → will be linked to \"{@case.Scenario}\".",
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

    private static string DescribeScope(ScopePlan scope, string kindLabel)
    {
        var created = scope.Cases.Count(c => c.Action == ImportAction.Create);
        if (!scope.Exists)
        {
            return $"New {kindLabel} scope \"{scope.Name}\" will be created with {Count(created, "test case")}.";
        }

        var parts = new List<string>();
        if (created > 0) parts.Add($"{created} new");
        var updated = scope.Cases.Count(c => c.Action == ImportAction.Update);
        if (updated > 0) parts.Add($"{updated} updated");
        var links = scope.Cases.Sum(c => c.NewLinks.Count);
        if (links > 0) parts.Add(Count(links, "new defect link"));
        if (scope.UnchangedCount > 0) parts.Add($"{scope.UnchangedCount} unchanged");

        return parts.Count == 0 || (parts.Count == 1 && scope.UnchangedCount > 0)
            ? $"Scope \"{scope.Name}\" already matches the file — nothing to change."
            : $"Scope \"{scope.Name}\" already exists — {string.Join(", ", parts)}.";
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
}
