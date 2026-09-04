using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Dashboard;

/// <summary>Why a test case is flagged for follow-up on the project dashboard.</summary>
public enum TestCaseFollowUpReason
{
    /// <summary>Marked passed, but still has an unresolved defect linked to it.</summary>
    PassedWithOpenDefect,

    /// <summary>Marked failed, but no defect has been raised against it.</summary>
    FailedNoDefect,

    /// <summary>Marked failed, but every linked defect is now fixed or not a defect — probably ready to retest.</summary>
    FailedReadyToRetest,
}

/// <summary>A test case that needs a QA/dev's attention, with the defects behind the flag.</summary>
public sealed record TestCaseFollowUp(
    TestCase Case, TestScope Scope, TestCaseFollowUpReason Reason, IReadOnlyList<Defect> RelatedDefects);

/// <summary>
/// The action items a project's dashboard surfaces: defects personally assigned to the
/// current user, and test cases whose result looks inconsistent with their linked defects.
/// </summary>
public sealed record ProjectActions(
    IReadOnlyList<Defect> DefectsToVerify,
    IReadOnlyList<Defect> DefectsToFix,
    IReadOnlyList<TestCaseFollowUp> TestCaseFollowUps)
{
    public int Count => DefectsToVerify.Count + DefectsToFix.Count + TestCaseFollowUps.Count;
}

/// <summary>
/// Computes the "what needs doing" section of the project dashboard. Read-only, so it
/// uses a context factory directly rather than going through <see cref="TestCaseService"/>
/// / <see cref="DefectService"/> (both of which are scoped to one aggregate).
/// </summary>
public sealed class ProjectActionsService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<ProjectActions> GetAsync(Guid projectId, string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var toVerify = await db.Defects
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.AssignedToId == userId && d.Status == DefectStatus.ToCheck)
            .ToListAsync(ct);

        var toFix = await db.Defects
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.AssignedToId == userId
                && (d.Status == DefectStatus.NotFixed || d.Status == DefectStatus.Fixing))
            .ToListAsync(ct);

        var cases = await db.TestCases
            .AsNoTracking()
            .Include(tc => tc.TestScope)
            .Include(tc => tc.Defects)
            .Where(tc => tc.TestScope!.ProjectId == projectId && tc.Result != TestResult.NotRun)
            .ToListAsync(ct);

        var followUps = new List<TestCaseFollowUp>();
        foreach (var tc in cases)
        {
            var open = tc.Defects.Where(IsOpen).ToList();
            var closed = tc.Defects.Where(d => !IsOpen(d)).ToList();

            if (tc.Result == TestResult.Passed && open.Count > 0)
            {
                followUps.Add(new TestCaseFollowUp(tc, tc.TestScope!, TestCaseFollowUpReason.PassedWithOpenDefect, open));
            }
            else if (tc.Result == TestResult.Failed && tc.Defects.Count == 0)
            {
                followUps.Add(new TestCaseFollowUp(tc, tc.TestScope!, TestCaseFollowUpReason.FailedNoDefect, []));
            }
            else if (tc.Result == TestResult.Failed && tc.Defects.Count > 0 && open.Count == 0)
            {
                followUps.Add(new TestCaseFollowUp(tc, tc.TestScope!, TestCaseFollowUpReason.FailedReadyToRetest, closed));
            }
        }

        return new ProjectActions(
            Ordered(toVerify),
            Ordered(toFix),
            followUps
                .OrderBy(f => f.Scope.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Case.Scenario, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    /// <summary>Not fixed and not dismissed as "not a defect" — i.e. still needs work.</summary>
    private static bool IsOpen(Defect d) => d.Status != DefectStatus.Fixed && d.Status != DefectStatus.NotADefect;

    /// <summary>Most severe first, then ascending by per-project number (mirrors <c>DefectService.Ordered</c>).</summary>
    private static IReadOnlyList<Defect> Ordered(IEnumerable<Defect> defects) =>
        defects.OrderByDescending(d => d.Severity).ThenBy(d => d.Number).ToList();
}
