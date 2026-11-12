using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;

namespace QaTracker.Web.TestCases;

/// <summary>Roll-up of a project's test plan for the dashboard.</summary>
public sealed record TestPlanSummary(
    int Scopes, int Cases, int Passed, int Failed, int NotRun, int Blocked = 0, int Inconclusive = 0);

/// <summary>
/// Previous / next not-yet-passed cases around a test case, for stepping through a test run —
/// or, once everything has passed, simply the adjacent cases. Blocked and inconclusive cases are
/// never stepped to. Either is null when there's nowhere to go. <see cref="ToAction"/> counts
/// every case in the project not passed, blocked or inconclusive (including the current one);
/// <see cref="Blocked"/> / <see cref="Inconclusive"/> count the skipped ones.
/// </summary>
public sealed record TestRunNavigation(
    TestCase? Previous, TestCase? Next, int ToAction, int Blocked = 0, int Inconclusive = 0);

/// <summary>
/// Reads and writes <see cref="TestScope"/> rows. Uses a context factory so each call
/// gets a short-lived context (safe under Blazor Server circuits).
/// </summary>
public sealed class TestScopeService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider timeProvider,
    ProjectService projects,
    AttachmentService attachments)
{
    /// <summary>Scopes for a project with their cases, in the project's chosen order (creation
    /// order until someone rearranges them). Every scope listing — the test cases page, test-run
    /// Previous/Next, CSV export, scope pickers — goes through here, so they all agree.</summary>
    public async Task<IReadOnlyList<TestScope>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scopes = await db.TestScopes
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .Include(s => s.Cases).ThenInclude(c => c.TestedBy) // tester names for the results export
            .ToListAsync(ct);

        foreach (var scope in scopes)
        {
            scope.Cases.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
        }

        // In memory: SQLite (used in tests) can't ORDER BY the DateTimeOffset tie-breaker.
        return scopes
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedUtc)
            .ToList();
    }

    /// <summary>
    /// Puts a project's scopes in the given order. Ids that aren't scopes of this project are
    /// ignored; scopes missing from the list (e.g. added by someone else while the reorder modal
    /// was open) keep their relative order after the listed ones. Returns false when the
    /// project has no scopes to order.
    /// </summary>
    public async Task<bool> ReorderAsync(Guid projectId, IReadOnlyList<Guid> orderedScopeIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scopes = (await db.TestScopes.Where(s => s.ProjectId == projectId).ToListAsync(ct))
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedUtc)
            .ToList();
        if (scopes.Count == 0)
        {
            return false;
        }

        var byId = scopes.ToDictionary(s => s.Id);
        var listed = orderedScopeIds.Distinct().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var ordered = listed.Concat(scopes.Except(listed)).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<TestScope?> GetAsync(Guid scopeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scope = await db.TestScopes
            .AsNoTracking()
            .Include(s => s.Cases)
            .FirstOrDefaultAsync(s => s.Id == scopeId, ct);

        scope?.Cases.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
        return scope;
    }

    public async Task<bool> ExistsInProjectAsync(Guid scopeId, Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TestScopes.AnyAsync(s => s.Id == scopeId && s.ProjectId == projectId, ct);
    }

    public async Task<TestScope> CreateAsync(
        Guid projectId,
        TestCaseKind kind,
        string name,
        string createdById,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var scope = new TestScope
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Kind = kind,
            Name = name.Trim(),
            CreatedById = createdById,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            // New scopes go to the bottom of the project's list.
            scope.SortOrder = (await db.TestScopes
                .Where(s => s.ProjectId == projectId)
                .MaxAsync(s => (int?)s.SortOrder, ct) ?? -1) + 1;
            db.TestScopes.Add(scope);
            ProjectHistory.Record(db, projectId, ProjectEventKind.ScopeCreated, createdById, now,
                toValue: scope.Name, subjectId: scope.Id);
            await db.SaveChangesAsync(ct);
        }

        // First item in the project moves it from Not Started to Active.
        await projects.MarkInFlightAsync(projectId, createdById, ct);
        return scope;
    }

    public async Task UpdateAsync(Guid scopeId, TestCaseKind kind, string name, string? actorId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scope = await db.TestScopes.FirstOrDefaultAsync(s => s.Id == scopeId, ct)
            ?? throw new InvalidOperationException($"Test scope {scopeId} not found.");

        var newName = name.Trim();
        scope.UpdatedUtc = timeProvider.GetUtcNow();
        if (scope.Name != newName)
        {
            ProjectHistory.Record(db, scope.ProjectId, ProjectEventKind.ScopeRenamed, actorId, scope.UpdatedUtc,
                scope.Name, newName, scope.Id);
        }

        scope.Kind = kind;
        scope.Name = newName;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a scope and its test cases (their history goes with them), recording
    /// the deletion in the project's history as <paramref name="actorId"/>.</summary>
    public async Task DeleteAsync(Guid scopeId, string? actorId = null, CancellationToken ct = default)
    {
        await attachments.PurgeForTestScopeAsync(scopeId, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scope = await db.TestScopes.AsNoTracking()
            .Where(s => s.Id == scopeId)
            .Select(s => new { s.ProjectId, s.Name, Cases = s.Cases.Count })
            .FirstOrDefaultAsync(ct);
        if (scope is null)
        {
            return;
        }

        ProjectHistory.Record(db, scope.ProjectId, ProjectEventKind.ScopeDeleted, actorId, timeProvider.GetUtcNow(),
            scope.Name, scope.Cases.ToString(System.Globalization.CultureInfo.InvariantCulture), scopeId);
        await db.SaveChangesAsync(ct);
        await db.TestScopes.Where(s => s.Id == scopeId).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Fingerprint of a project's test plan (<see cref="LiveVersion"/>) for the live test plan and
    /// scope pages: its scopes (names, kinds, order) and its test cases — every create, edit, move
    /// and result change adds a history entry, so counting those catches them without reading
    /// every case.
    /// </summary>
    public async Task<string> GetPlanLiveVersionAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scopes = (await db.TestScopes.AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .Select(s => new { s.Id, s.Name, s.Kind, s.SortOrder })
                .ToListAsync(ct))
            .OrderBy(s => s.Id)
            .Select(s => $"{s.Id}:{s.Kind}:{s.SortOrder}:{s.Name}")
            .ToList();
        var cases = await db.TestCases.CountAsync(t => t.TestScope!.ProjectId == projectId, ct);
        var events = await db.TestCaseEvents.CountAsync(e => e.TestCase!.TestScope!.ProjectId == projectId, ct);
        return LiveVersion.Of(scopes, cases, events);
    }

    public async Task<TestPlanSummary> SummariseProjectAsync(Guid projectId, CancellationToken ct = default) =>
        (await SummariseProjectsAsync([projectId], ct))[projectId];

    /// <summary>
    /// <see cref="TestPlanSummary"/> for several projects at once (the All Projects list), in two
    /// grouped queries rather than two per project. Every requested id gets an entry — all zeros
    /// for a project with no test plan yet.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, TestPlanSummary>> SummariseProjectsAsync(
        IEnumerable<Guid> projectIds, CancellationToken ct = default)
    {
        var ids = projectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, TestPlanSummary>();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var scopeCounts = await db.TestScopes
            .Where(s => ids.Contains(s.ProjectId))
            .GroupBy(s => s.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

        var byResult = await db.TestCases
            .Where(tc => ids.Contains(tc.TestScope!.ProjectId))
            .GroupBy(tc => new { tc.TestScope!.ProjectId, tc.Result })
            .Select(g => new { g.Key.ProjectId, g.Key.Result, Count = g.Count() })
            .ToListAsync(ct);

        return ids.ToDictionary(id => id, id =>
        {
            var rows = byResult.Where(x => x.ProjectId == id).ToList();
            int Count(TestResult r) => rows.FirstOrDefault(x => x.Result == r)?.Count ?? 0;

            return new TestPlanSummary(
                Scopes: scopeCounts.GetValueOrDefault(id),
                Cases: rows.Sum(x => x.Count),
                Passed: Count(TestResult.Passed),
                Failed: Count(TestResult.Failed),
                NotRun: Count(TestResult.NotRun),
                Blocked: Count(TestResult.Blocked),
                Inconclusive: Count(TestResult.Inconclusive));
        });
    }

    /// <summary>
    /// Finds the cases a tester would step to from <paramref name="testCaseId"/>, skipping
    /// anything already passed, blocked (it can't be run) or inconclusive (it's waiting on
    /// business feedback). When the current scope
    /// has none left in that direction, both carry on into earlier / later scopes (same order as
    /// the test-cases page). Once every runnable case has passed there's nothing to skip, so it
    /// steps through all of them in order — still leaving out blocked and inconclusive ones.
    /// </summary>
    public async Task<TestRunNavigation> GetRunNavigationAsync(
        Guid projectId, Guid testCaseId, CancellationToken ct = default)
    {
        var scopes = (await ListForProjectAsync(projectId, ct)).ToList();

        var scopeIndex = scopes.FindIndex(s => s.Cases.Any(c => c.Id == testCaseId));
        if (scopeIndex < 0)
        {
            return new TestRunNavigation(null, null, 0);
        }

        var cases = scopes[scopeIndex].Cases;
        var caseIndex = cases.FindIndex(c => c.Id == testCaseId);

        var all = scopes.SelectMany(s => s.Cases).ToList();
        var blocked = all.Count(c => c.Result == TestResult.Blocked);
        var inconclusive = all.Count(c => c.Result == TestResult.Inconclusive);
        var toAction = all.Count(c => c.Result is not (TestResult.Passed or TestResult.Blocked or TestResult.Inconclusive));
        Func<TestCase, bool> stepTo = toAction == 0
            ? c => c.Result is not (TestResult.Blocked or TestResult.Inconclusive)
            : c => c.Result is not (TestResult.Passed or TestResult.Blocked or TestResult.Inconclusive);

        var previous = scopes.Take(scopeIndex).SelectMany(s => s.Cases)
            .Concat(cases.Take(caseIndex))
            .LastOrDefault(stepTo);
        var next = cases.Skip(caseIndex + 1)
            .Concat(scopes.Skip(scopeIndex + 1).SelectMany(s => s.Cases))
            .FirstOrDefault(stepTo);

        return new TestRunNavigation(previous, next, toAction, blocked, inconclusive);
    }
}
