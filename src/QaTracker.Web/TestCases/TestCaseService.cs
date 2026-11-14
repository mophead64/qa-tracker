using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;

namespace QaTracker.Web.TestCases;

/// <summary>Editable fields of a test case.</summary>
public sealed record TestCaseInput(string Scenario, string? Steps);

/// <summary>A comment on a test case, with its author's display name resolved.</summary>
public sealed record TestCaseCommentView(
    Guid Id, string AuthorId, string AuthorName, string Body, DateTimeOffset CreatedUtc,
    IReadOnlyList<CommentAttachmentView> Attachments, IReadOnlyList<CommentReactionView> Reactions);

/// <summary>
/// Reads and writes <see cref="TestCase"/> rows within a <see cref="TestScope"/>. Uses a
/// context factory so each call gets a short-lived context (safe under Blazor Server
/// circuits).
/// </summary>
public sealed class TestCaseService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider timeProvider,
    AttachmentService attachments,
    NotificationService notifications)
{
    public async Task<IReadOnlyList<TestCase>> ListForScopeAsync(Guid scopeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.TestCases
            .AsNoTracking()
            .Where(tc => tc.TestScopeId == scopeId)
            .ToListAsync(ct);

        // SQLite (used in tests) can't ORDER BY the DateTimeOffset column.
        return list.OrderBy(tc => tc.CreatedUtc).ToList();
    }

    public async Task<TestCase?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TestCases
            .AsNoTracking()
            .Include(tc => tc.TestedBy)
            .FirstOrDefaultAsync(tc => tc.Id == id, ct);
    }

    /// <summary>Whether the test case exists and sits in a scope of <paramref name="projectId"/>.</summary>
    public async Task<bool> BelongsToProjectAsync(Guid testCaseId, Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TestCases.AnyAsync(tc => tc.Id == testCaseId && tc.TestScope!.ProjectId == projectId, ct);
    }

    public Task<TestCase> CreateAsync(
        Guid scopeId,
        TestCaseInput input,
        string createdById,
        CancellationToken ct = default) =>
        CreateAsync(scopeId, input, createdById, imported: false, ct);

    /// <summary>Creates a test case. <paramref name="imported"/> marks one created by a CSV import:
    /// its history says so, and the project's activity shows the import as one entry instead.</summary>
    public async Task<TestCase> CreateAsync(
        Guid scopeId,
        TestCaseInput input,
        string createdById,
        bool imported,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var testCase = new TestCase
        {
            Id = Guid.NewGuid(),
            TestScopeId = scopeId,
            Scenario = input.Scenario.Trim(),
            Steps = Normalize(input.Steps),
            Result = TestResult.NotRun,
            CreatedById = createdById,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        // The next T-number across the project; (ProjectId, Number) is unique, so a concurrent create
        // that takes the same number fails the save and we retry with the next one.
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            testCase.ProjectId = await db.TestScopes
                .Where(s => s.Id == scopeId)
                .Select(s => s.ProjectId)
                .FirstOrDefaultAsync(ct);
            testCase.Number = (await db.TestCases
                .Where(tc => tc.ProjectId == testCase.ProjectId)
                .MaxAsync(tc => (int?)tc.Number, ct) ?? 0) + 1;
            db.TestCases.Add(testCase);
            TestCaseHistory.Record(db, testCase.Id,
                imported ? TestCaseEventKind.Imported : TestCaseEventKind.Created, createdById, now);
            try
            {
                await db.SaveChangesAsync(ct);
                return testCase;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // lost the race for this Number — recompute and retry
            }
        }
    }

    /// <summary>Saves the scenario and steps. A history entry is recorded (as
    /// <paramref name="editedById"/>) only when either actually changed.</summary>
    public async Task UpdateAsync(Guid id, TestCaseInput input, string? editedById = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var testCase = await db.TestCases.FirstOrDefaultAsync(tc => tc.Id == id, ct)
            ?? throw new InvalidOperationException($"Test case {id} not found.");

        var scenario = input.Scenario.Trim();
        var steps = Normalize(input.Steps);
        var scenarioChanged = !string.Equals(testCase.Scenario, scenario, StringComparison.Ordinal);
        var stepsChanged = !string.Equals(testCase.Steps, steps, StringComparison.Ordinal);

        var now = timeProvider.GetUtcNow();
        testCase.Scenario = scenario;
        testCase.Steps = steps;
        testCase.UpdatedUtc = now;

        if (scenarioChanged || stepsChanged)
        {
            var what = (scenarioChanged, stepsChanged) switch
            {
                (true, true) => TestCaseHistory.EditedScenarioAndSteps,
                (true, false) => TestCaseHistory.EditedScenario,
                _ => TestCaseHistory.EditedSteps,
            };
            TestCaseHistory.Record(db, id, TestCaseEventKind.Edited, editedById, now, toValue: what);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Records a result. Any outcome other than Not run also records who set it
    /// (<paramref name="testedById"/>) and when, as the case's "Tested by"; going back to Not
    /// run clears that.</summary>
    public async Task SetResultAsync(Guid id, TestResult result, string? testedById = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var testCase = await db.TestCases.FirstOrDefaultAsync(tc => tc.Id == id, ct)
            ?? throw new InvalidOperationException($"Test case {id} not found.");

        var now = timeProvider.GetUtcNow();
        var previousResult = testCase.Result;
        var previousTesterId = testCase.TestedById;
        if (testCase.Result != result)
        {
            TestCaseHistory.Record(db, id, TestCaseEventKind.ResultChanged, testedById, now,
                testCase.Result.ToString(), result.ToString());
        }

        testCase.Result = result;
        testCase.UpdatedUtc = now;
        if (result == TestResult.NotRun)
        {
            testCase.TestedById = null;
            testCase.TestedUtc = null;
        }
        else
        {
            testCase.TestedById = testedById;
            testCase.TestedUtc = now;
        }

        await db.SaveChangesAsync(ct);

        // Whoever set the result before hears when someone else changes it.
        if (result != previousResult && previousTesterId is not null)
        {
            await notifications.NotifyTestCaseResultChangedAsync(testCase, previousTesterId, previousResult, testedById, ct);
        }
    }

    /// <summary>
    /// Moves a test case into another scope of the same project, keeping its result, tester,
    /// comments, files and linked defects. Returns the scope it's now in, or null (and changes
    /// nothing) when the case or target doesn't exist or the target is in a different project.
    /// </summary>
    public async Task<TestScope?> MoveAsync(Guid id, Guid targetScopeId, string? movedById = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var testCase = await db.TestCases
            .Include(tc => tc.TestScope)
            .FirstOrDefaultAsync(tc => tc.Id == id, ct);
        var target = await db.TestScopes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == targetScopeId, ct);
        if (testCase is null || target is null || target.ProjectId != testCase.TestScope!.ProjectId)
        {
            return null;
        }

        if (testCase.TestScopeId != targetScopeId)
        {
            var now = timeProvider.GetUtcNow();
            TestCaseHistory.Record(db, id, TestCaseEventKind.Moved, movedById, now,
                TestCaseDisplay.ScopeLabel(testCase.TestScope!), TestCaseDisplay.ScopeLabel(target));
            testCase.TestScopeId = targetScopeId;
            testCase.UpdatedUtc = now;
            await db.SaveChangesAsync(ct);
        }

        return target;
    }

    /// <summary>Deletes a test case (its own history goes with it), recording the deletion in
    /// the project's history as <paramref name="actorId"/>.</summary>
    public async Task DeleteAsync(Guid id, string? actorId = null, CancellationToken ct = default)
    {
        await attachments.PurgeForTestCaseAsync(id, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var testCase = await db.TestCases.AsNoTracking()
            .Where(tc => tc.Id == id)
            .Select(tc => new { tc.TestScope!.ProjectId, tc.Scenario, tc.Number })
            .FirstOrDefaultAsync(ct);
        if (testCase is null)
        {
            return;
        }

        ProjectHistory.Record(db, testCase.ProjectId, ProjectEventKind.TestCaseDeleted, actorId,
            timeProvider.GetUtcNow(),
            fromValue: testCase.Number > 0 ? $"{TestCaseDisplay.CaseRef(testCase.Number)} {testCase.Scenario}" : testCase.Scenario);
        await db.SaveChangesAsync(ct);
        await db.TestCases.Where(tc => tc.Id == id).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Fingerprint of everything the test case page shows (<see cref="LiveVersion"/>): the case,
    /// its history (every result change, edit, move, file and defect link adds an entry), its
    /// comments and their reactions, its linked defects as they are now, and the project's run
    /// progress (the page's Previous / Next and "cases to action"). Null when the case is gone.
    /// </summary>
    public async Task<string?> GetLiveVersionAsync(Guid testCaseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tc = await db.TestCases.AsNoTracking()
            .Where(t => t.Id == testCaseId)
            .Select(t => new { t.Scenario, t.Steps, t.Result, t.TestedById, t.TestedUtc, t.TestScopeId, t.TestScope!.ProjectId })
            .FirstOrDefaultAsync(ct);
        if (tc is null)
        {
            return null;
        }

        var events = await db.TestCaseEvents.CountAsync(e => e.TestCaseId == testCaseId, ct);
        var comments = (await db.TestCaseComments.Where(c => c.TestCaseId == testCaseId).Select(c => c.Id).ToListAsync(ct))
            .Order().ToList();
        var reactions = (await db.CommentReactions
                .Where(r => r.TestCaseComment!.TestCaseId == testCaseId)
                .Select(r => new { r.TestCaseCommentId, r.UserId, r.Reaction })
                .ToListAsync(ct))
            .Select(r => $"{r.TestCaseCommentId}:{r.UserId}:{r.Reaction}").Order().ToList();
        var defects = (await db.Defects
                .Where(d => d.TestCases.Any(t => t.Id == testCaseId))
                .Select(d => new { d.Id, d.Number, d.Summary, d.Status, d.Severity })
                .ToListAsync(ct))
            .Select(d => $"{d.Id}:{d.Number}:{d.Status}:{d.Severity}:{d.Summary}").Order().ToList();
        // Anyone recording a result anywhere in the project moves the run navigation on.
        var projectResults = await db.TestCaseEvents.CountAsync(
            e => e.Kind == TestCaseEventKind.ResultChanged && e.TestCase!.TestScope!.ProjectId == tc.ProjectId, ct);
        var projectCases = await db.TestCases.CountAsync(t => t.TestScope!.ProjectId == tc.ProjectId, ct);

        return LiveVersion.Of(tc.Scenario, tc.Steps, tc.Result, tc.TestedById, tc.TestedUtc, tc.TestScopeId,
            events, comments, reactions, defects, projectResults, projectCases);
    }

    /// <summary>The case's history, newest first.</summary>
    public async Task<IReadOnlyList<TestCaseEventView>> ListHistoryAsync(Guid testCaseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var events = await db.TestCaseEvents
            .AsNoTracking()
            .Where(e => e.TestCaseId == testCaseId)
            .Include(e => e.Actor)
            .ToListAsync(ct);

        // SQLite (used in tests) can't ORDER BY the DateTimeOffset column. Kind breaks ties so a
        // creation recorded in the same instant as something else still sorts last.
        return events
            .OrderByDescending(e => e.OccurredUtc)
            .ThenByDescending(e => e.Kind != TestCaseEventKind.Created)
            .Select(e => new TestCaseEventView(
                e.Id, e.Kind, e.ActorId, DisplayName(e.Actor),
                e.OccurredUtc, e.FromValue, e.ToValue))
            .ToList();
    }

    public async Task<IReadOnlyList<TestCaseCommentView>> ListCommentsAsync(Guid testCaseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var comments = await db.TestCaseComments
            .AsNoTracking()
            .Where(c => c.TestCaseId == testCaseId)
            .Include(c => c.Author)
            .Include(c => c.Attachments)
            .Include(c => c.Reactions).ThenInclude(r => r.User)
            .AsSplitQuery()
            .ToListAsync(ct);

        return comments
            .OrderBy(c => c.CreatedUtc)
            .Select(c => new TestCaseCommentView(
                c.Id, c.AuthorId, DisplayName(c.Author), c.Body.Trim(), c.CreatedUtc,
                c.Attachments.OrderBy(a => a.SortOrder).Select(CommentAttachmentView.From).ToList(),
                CommentReactionView.ListFrom(c.Reactions)))
            .ToList();
    }

    public async Task<Guid> AddCommentAsync(Guid testCaseId, string authorId, string body,
        IReadOnlyCollection<string>? mentionedUserIds = null, CancellationToken ct = default)
    {
        var comment = new TestCaseComment
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            AuthorId = authorId,
            Body = body.Trim(),
            CreatedUtc = timeProvider.GetUtcNow(),
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.TestCaseComments.Add(comment);
        await db.SaveChangesAsync(ct);

        var testCase = await db.TestCases.AsNoTracking()
            .Include(tc => tc.TestScope)
            .FirstOrDefaultAsync(tc => tc.Id == testCaseId, ct);
        if (testCase?.TestScope is { } scope)
        {
            var mentioned = await notifications.NotifyMentionedAsync(
                scope.ProjectId, null, testCase, authorId, comment.Body, mentionedUserIds, ct);
            await notifications.NotifyTestCaseCommentAsync(testCase, authorId, mentioned, ct);
        }

        return comment.Id;
    }

    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default)
    {
        await attachments.PurgeForCommentAsync(AttachmentOwner.TestCaseComment, commentId, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.TestCaseComments.Where(c => c.Id == commentId).ExecuteDeleteAsync(ct);
    }

    private static string DisplayName(ApplicationUser? user)
    {
        if (user is null)
        {
            return "Unknown";
        }

        return !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.UserName ?? "Unknown";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
