namespace QaTracker.Web.TestCases;

/// <summary>Severity of an <see cref="ImportIssue"/> surfaced on the import preview.</summary>
public enum ImportSeverity
{
    /// <summary>Blocks the import until the CSV is fixed.</summary>
    Error,

    /// <summary>Something the user should know, but the import can still proceed.</summary>
    Warning,

    /// <summary>Neutral "here's what will happen" note.</summary>
    Info,
}

/// <summary>Whether importing a case will create a new row or overwrite an existing one.</summary>
public enum ImportAction
{
    Create,
    Update,
}

/// <summary>One message on the import preview, optionally tied to a source row / scope / case.</summary>
public sealed record ImportIssue(
    ImportSeverity Severity,
    string Message,
    int? Row = null,
    string? Scope = null,
    string? Scenario = null);

// --- Parser output (no database) -------------------------------------------------

/// <summary>A test case as read from the CSV, before it's matched against the database.</summary>
public sealed record ParsedCase(string Scenario, string? Steps, IReadOnlyList<string> DefectTokens, int FirstRow);

/// <summary>A scope as read from the CSV, with the cases parsed under it.</summary>
public sealed record ParsedScope(string Name, TestCaseKind Kind, IReadOnlyList<ParsedCase> Cases);

/// <summary>
/// The structural result of parsing an import CSV. <see cref="Errors"/> are blocking and come
/// purely from the file's shape (missing columns, missing/invalid values, duplicated or
/// interleaved scopes / scenarios). Database-aware checks happen later in
/// <see cref="TestCaseImportService.BuildPlanAsync"/>.
/// </summary>
public sealed record ParsedImport(IReadOnlyList<ParsedScope> Scopes, IReadOnlyList<ImportIssue> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}

// --- Plan (database-aware) ------------------------------------------------------

/// <summary>A resolved defect link for the preview, e.g. D-3 → "some scenario".</summary>
public sealed record DefectLinkPlan(int Number, bool AlreadyLinked);

/// <summary>What importing one case will do.</summary>
public sealed record CasePlan(
    string Scenario,
    string? Steps,
    ImportAction Action,
    IReadOnlyList<DefectLinkPlan> DefectLinks);

/// <summary>What importing one scope will do.</summary>
public sealed record ScopePlan(
    string Name,
    TestCaseKind Kind,
    bool Exists,
    IReadOnlyList<CasePlan> Cases);

/// <summary>
/// The full, database-aware preview of an import: what will be created / updated / linked,
/// plus every issue to show. The import is allowed only when there are no
/// <see cref="ImportSeverity.Error"/> issues.
/// </summary>
public sealed record ImportPlan(IReadOnlyList<ScopePlan> Scopes, IReadOnlyList<ImportIssue> Issues)
{
    public IEnumerable<ImportIssue> Errors => Issues.Where(i => i.Severity == ImportSeverity.Error);

    public IEnumerable<ImportIssue> Warnings => Issues.Where(i => i.Severity == ImportSeverity.Warning);

    public IEnumerable<ImportIssue> Infos => Issues.Where(i => i.Severity == ImportSeverity.Info);

    public bool CanImport => Issues.All(i => i.Severity != ImportSeverity.Error);

    public int ScopesToCreate => Scopes.Count(s => !s.Exists);

    public int CasesToCreate => Scopes.Sum(s => s.Cases.Count(c => c.Action == ImportAction.Create));

    public int CasesToUpdate => Scopes.Sum(s => s.Cases.Count(c => c.Action == ImportAction.Update));
}

/// <summary>Outcome counts after a successful <see cref="TestCaseImportService.ApplyAsync"/>.</summary>
public sealed record ImportResult(
    bool Committed,
    int ScopesCreated,
    int CasesCreated,
    int CasesUpdated,
    int DefectsLinked,
    IReadOnlyList<ImportIssue> Issues)
{
    public static ImportResult Blocked(IReadOnlyList<ImportIssue> issues) => new(false, 0, 0, 0, 0, issues);
}
