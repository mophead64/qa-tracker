using QaTracker.Web.Defects;

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
    DefectService defects)
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

            if (existing is not null && existing.Kind != scope.Kind)
            {
                issues.Add(new(ImportSeverity.Error,
                    $"Scope \"{scope.Name}\" already exists as {TestCaseDisplay.KindLabel(existing.Kind)}, but the file says {scopeLabel}. " +
                    "Change the file or the scope so they agree.", Scope: scope.Name));
            }
            else if (existing is not null)
            {
                issues.Add(new(ImportSeverity.Info,
                    $"Scope \"{scope.Name}\" already exists — {Count(scope.Cases.Count, "test case")} will be added or updated.", Scope: scope.Name));
            }
            else
            {
                issues.Add(new(ImportSeverity.Info,
                    $"New {scopeLabel} scope \"{scope.Name}\" will be created with {Count(scope.Cases.Count, "test case")}.", Scope: scope.Name));
            }

            var casePlans = new List<CasePlan>();
            foreach (var @case in scope.Cases)
            {
                var existingCase = existing?.Cases.FirstOrDefault(c => Same(c.Scenario, @case.Scenario));
                var action = existingCase is null ? ImportAction.Create : ImportAction.Update;

                if (existingCase is not null)
                {
                    issues.Add(new(ImportSeverity.Warning,
                        $"Test case \"{@case.Scenario}\" already exists in \"{scope.Name}\" — its steps will be overwritten.",
                        @case.FirstRow, scope.Name, @case.Scenario));
                }

                var links = new List<DefectLinkPlan>();
                foreach (var token in @case.DefectTokens)
                {
                    AppraiseLink(token, @case, scope.Name, existingCase, defectsByNumber, issues, links);
                }

                casePlans.Add(new CasePlan(@case.Scenario, @case.Steps, action, links));
            }

            scopePlans.Add(new ScopePlan(scope.Name, scope.Kind, existing is not null, casePlans));
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
                    var created = await cases.CreateAsync(scopeId, input, userId, ct);
                    caseId = created.Id;
                    casesCreated++;
                }
                else
                {
                    await cases.UpdateAsync(existingCase.Id, input, ct);
                    caseId = existingCase.Id;
                    casesUpdated++;
                }

                foreach (var token in @case.DefectTokens)
                {
                    if (TryParseDefectNumber(token, out var number)
                        && defectsByNumber.TryGetValue(number, out var defect))
                    {
                        var wasLinked = defect.TestCases.Any(tc => tc.Id == caseId);
                        await defects.LinkTestCaseAsync(defect.Id, caseId, ct);
                        if (!wasLinked)
                        {
                            defectsLinked++;
                        }
                    }
                }
            }
        }

        return new ImportResult(true, scopesCreated, casesCreated, casesUpdated, defectsLinked,
            plan.Issues.Where(i => i.Severity != ImportSeverity.Error).ToList());
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

        var alreadyLinked = existingCase is not null && defect.TestCases.Any(tc => tc.Id == existingCase.Id);
        if (alreadyLinked)
        {
            issues.Add(new(ImportSeverity.Info,
                $"{DefectDisplay.Ref(number)} is already linked to \"{@case.Scenario}\".",
                @case.FirstRow, scopeName, @case.Scenario));
        }
        else if (defect.TestCases.Count > 0)
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

        links.Add(new DefectLinkPlan(number, alreadyLinked));
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

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
}
