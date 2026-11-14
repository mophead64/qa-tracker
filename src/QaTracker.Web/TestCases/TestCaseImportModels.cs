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

/// <summary>What importing a case will do to it.</summary>
public enum ImportAction
{
    /// <summary>A new test case.</summary>
    Create,

    /// <summary>An existing case whose scenario wording or steps differ from the file.</summary>
    Update,

    /// <summary>An existing case the file matches exactly — left alone (any new defect links still apply).</summary>
    Unchanged,
}

/// <summary>One message on the import preview, optionally tied to a source row / scope / case.</summary>
public sealed record ImportIssue(
    ImportSeverity Severity,
    string Message,
    int? Row = null,
    string? Scope = null,
    string? Scenario = null);

// --- Parser output (no database) -------------------------------------------------

/// <summary>A test case as read from the CSV, before it's matched against the database.
/// <see cref="Number"/> is its "T-n" ID when the file gave one.</summary>
public sealed record ParsedCase(string Scenario, string? Steps, IReadOnlyList<string> DefectTokens, int FirstRow, int? Number = null);

/// <summary>A scope as read from the CSV, with the cases parsed under it. <see cref="Kind"/> is
/// null when no row gave a type; <see cref="Number"/> is its "S-n" ID when the file gave one.</summary>
public sealed record ParsedScope(string Name, TestCaseKind? Kind, IReadOnlyList<ParsedCase> Cases, int FirstRow = 0, int? Number = null);

/// <summary>
/// The structural result of parsing an import CSV. <see cref="Errors"/> are blocking and come
/// purely from the file's shape (missing columns, missing/invalid values, duplicate names or IDs,
/// interleaved scopes); <see cref="Warnings"/> are things in the file worth a second look (the
/// same scenario in two scopes). Database-aware checks happen later in
/// <see cref="TestCaseImportService.BuildPlanAsync"/>.
/// </summary>
public sealed record ParsedImport(IReadOnlyList<ParsedScope> Scopes, IReadOnlyList<ImportIssue> Errors)
{
    public IReadOnlyList<ImportIssue> Warnings { get; init; } = [];

    public bool HasErrors => Errors.Count > 0;
}

// --- Plan (database-aware) ------------------------------------------------------

/// <summary>A resolved defect link for the preview, e.g. D-3 → "some scenario".</summary>
public sealed record DefectLinkPlan(int Number, bool AlreadyLinked);

/// <summary>What importing one case will do. For an existing case, <see cref="ExistingScenario"/> /
/// <see cref="ExistingSteps"/> hold what's there now, so the preview can show the difference;
/// <see cref="ExistingId"/> / <see cref="Number"/> identify it, and <see cref="MovedFrom"/> names
/// the scope it's in now when the file puts it in another.</summary>
public sealed record CasePlan(
    string Scenario,
    string? Steps,
    ImportAction Action,
    IReadOnlyList<DefectLinkPlan> DefectLinks,
    string? ExistingScenario = null,
    string? ExistingSteps = null)
{
    public Guid? ExistingId { get; init; }

    /// <summary>The existing case's "T-n" number (null for a new case — it gets one on import).</summary>
    public int? Number { get; init; }

    /// <summary>The scope the existing case is in now, when importing moves it to this one.</summary>
    public string? MovedFrom { get; init; }

    /// <summary>Source row, to tie the preview's messages to the case.</summary>
    public int FirstRow { get; init; }

    /// <summary>Links the import will add (not already in place).</summary>
    public IReadOnlyList<DefectLinkPlan> NewLinks => DefectLinks.Where(l => !l.AlreadyLinked).ToList();

    public bool ScenarioChanged => ExistingScenario is not null && !string.Equals(ExistingScenario, Scenario, StringComparison.Ordinal);

    public bool StepsChanged => Action == ImportAction.Update
        && !LineDiff.Lines(ExistingSteps).SequenceEqual(LineDiff.Lines(Steps));

    /// <summary>Line diff of the steps, existing → file (only meaningful for an update).</summary>
    public IReadOnlyList<DiffLine> StepsDiff => LineDiff.Compute(LineDiff.Lines(ExistingSteps), LineDiff.Lines(Steps));

    /// <summary>Whether importing touches this case at all.</summary>
    public bool HasChanges => Action != ImportAction.Unchanged || NewLinks.Count > 0 || MovedFrom is not null;
}

/// <summary>What importing one scope will do. <see cref="ExistingName"/> is set when the file
/// renames an existing scope (matched by its ID); <see cref="KindAssumed"/> when the file gave no
/// type for a new scope, so it'll be Functional.</summary>
public sealed record ScopePlan(
    string Name,
    TestCaseKind Kind,
    bool Exists,
    IReadOnlyList<CasePlan> Cases)
{
    public Guid? ExistingId { get; init; }

    /// <summary>The existing scope's "S-n" number (null for a new scope — it gets one on import).</summary>
    public int? Number { get; init; }

    public string? ExistingName { get; init; }

    public bool KindAssumed { get; init; }

    /// <summary>The file renames this scope (by its ID) to a name another scope already has — an
    /// error; the preview doesn't describe the rename as happening.</summary>
    public bool RenameBlocked { get; init; }

    public bool Renamed => ExistingName is not null && !string.Equals(ExistingName, Name, StringComparison.Ordinal);

    public bool HasChanges => !Exists || Renamed || Cases.Any(c => c.HasChanges);

    /// <summary>Cases the import leaves exactly as they are (hidden on the preview).</summary>
    public int UnchangedCount => Cases.Count(c => !c.HasChanges);
}

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

    public int CasesUnchanged => Scopes.Sum(s => s.UnchangedCount);

    public int LinksToAdd => Scopes.Sum(s => s.Cases.Sum(c => c.NewLinks.Count));

    public int ScopesToRename => Scopes.Count(s => s.Renamed);

    public int CasesToMove => Scopes.Sum(s => s.Cases.Count(c => c.MovedFrom is not null));

    /// <summary>The messages about one case in the preview (matched on scope + scenario).</summary>
    public IEnumerable<ImportIssue> IssuesFor(ScopePlan scope, CasePlan @case) => Issues.Where(i =>
        i.Severity != ImportSeverity.Info
        && string.Equals(i.Scope, scope.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(i.Scenario, @case.Scenario, StringComparison.OrdinalIgnoreCase));

    /// <summary>False when the file already matches the project exactly — nothing to import.</summary>
    public bool HasChanges => Scopes.Any(s => s.HasChanges);
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
    public int ScopesRenamed { get; init; }

    public int CasesMoved { get; init; }

    public static ImportResult Blocked(IReadOnlyList<ImportIssue> issues) => new(false, 0, 0, 0, 0, issues);
}
