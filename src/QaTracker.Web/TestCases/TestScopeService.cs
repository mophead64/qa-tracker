using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;

namespace QaTracker.Web.TestCases;

/// <summary>Roll-up of a project's test plan for the dashboard.</summary>
public sealed record TestPlanSummary(int Scopes, int Cases, int Passed, int Failed, int NotRun);

/// <summary>
/// Previous / next not-yet-passed cases around a test case, for stepping through a test run —
/// or, once everything has passed, simply the adjacent cases. Either is null when there's
/// nowhere to go. <see cref="ToAction"/> counts every case in the
/// project not yet passed (including the current one).
/// </summary>
public sealed record TestRunNavigation(TestCase? Previous, TestCase? Next, int ToAction);

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
    /// <summary>Scopes for a project with their cases, ordered by kind then name.</summary>
    public async Task<IReadOnlyList<TestScope>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scopes = await db.TestScopes
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .Include(s => s.Cases)
            .ToListAsync(ct);

        foreach (var scope in scopes)
        {
            scope.Cases.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
        }

        return scopes
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            db.TestScopes.Add(scope);
            await db.SaveChangesAsync(ct);
        }

        // First item in the project moves it from Inactive to Active.
        await projects.MarkInFlightAsync(projectId, ct);
        return scope;
    }

    public async Task UpdateAsync(Guid scopeId, TestCaseKind kind, string name, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scope = await db.TestScopes.FirstOrDefaultAsync(s => s.Id == scopeId, ct)
            ?? throw new InvalidOperationException($"Test scope {scopeId} not found.");

        scope.Kind = kind;
        scope.Name = name.Trim();
        scope.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid scopeId, CancellationToken ct = default)
    {
        await attachments.PurgeForTestScopeAsync(scopeId, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.TestScopes.Where(s => s.Id == scopeId).ExecuteDeleteAsync(ct);
    }

    public async Task<TestPlanSummary> SummariseProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var scopeCount = await db.TestScopes.CountAsync(s => s.ProjectId == projectId, ct);

        var byResult = await db.TestCases
            .Where(tc => tc.TestScope!.ProjectId == projectId)
            .GroupBy(tc => tc.Result)
            .Select(g => new { Result = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(TestResult r) => byResult.FirstOrDefault(x => x.Result == r)?.Count ?? 0;

        return new TestPlanSummary(
            Scopes: scopeCount,
            Cases: byResult.Sum(x => x.Count),
            Passed: Count(TestResult.Passed),
            Failed: Count(TestResult.Failed),
            NotRun: Count(TestResult.NotRun));
    }

    /// <summary>
    /// Finds the cases a tester would step to from <paramref name="testCaseId"/>, skipping
    /// anything already passed. When the current scope has none left in that direction, both
    /// carry on into earlier / later scopes (same order as the test-cases page). Once every case
    /// has passed there's nothing to skip, so it steps through all of them in order.
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

        var toAction = scopes.Sum(s => s.Cases.Count(c => c.Result != TestResult.Passed));
        Func<TestCase, bool> stepTo = toAction == 0 ? _ => true : c => c.Result != TestResult.Passed;

        var previous = scopes.Take(scopeIndex).SelectMany(s => s.Cases)
            .Concat(cases.Take(caseIndex))
            .LastOrDefault(stepTo);
        var next = cases.Skip(caseIndex + 1)
            .Concat(scopes.Skip(scopeIndex + 1).SelectMany(s => s.Cases))
            .FirstOrDefault(stepTo);

        return new TestRunNavigation(previous, next, toAction);
    }
}
