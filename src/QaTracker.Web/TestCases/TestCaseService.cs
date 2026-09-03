using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.TestCases;

/// <summary>Editable fields of a test case.</summary>
public sealed record TestCaseInput(string Scenario, string? Steps);

/// <summary>A comment on a test case, with its author's display name resolved.</summary>
public sealed record TestCaseCommentView(Guid Id, string AuthorId, string AuthorName, string Body, DateTimeOffset CreatedUtc);

/// <summary>
/// Reads and writes <see cref="TestCase"/> rows within a <see cref="TestScope"/>. Uses a
/// context factory so each call gets a short-lived context (safe under Blazor Server
/// circuits).
/// </summary>
public sealed class TestCaseService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider timeProvider)
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
            .FirstOrDefaultAsync(tc => tc.Id == id, ct);
    }

    public async Task<TestCase> CreateAsync(
        Guid scopeId,
        TestCaseInput input,
        string createdById,
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

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.TestCases.Add(testCase);
        await db.SaveChangesAsync(ct);
        return testCase;
    }

    public async Task UpdateAsync(Guid id, TestCaseInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var testCase = await db.TestCases.FirstOrDefaultAsync(tc => tc.Id == id, ct)
            ?? throw new InvalidOperationException($"Test case {id} not found.");

        testCase.Scenario = input.Scenario.Trim();
        testCase.Steps = Normalize(input.Steps);
        testCase.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    public async Task SetResultAsync(Guid id, TestResult result, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var testCase = await db.TestCases.FirstOrDefaultAsync(tc => tc.Id == id, ct)
            ?? throw new InvalidOperationException($"Test case {id} not found.");

        testCase.Result = result;
        testCase.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.TestCases.Where(tc => tc.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<TestCaseCommentView>> ListCommentsAsync(Guid testCaseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var comments = await db.TestCaseComments
            .AsNoTracking()
            .Where(c => c.TestCaseId == testCaseId)
            .Include(c => c.Author)
            .ToListAsync(ct);

        return comments
            .OrderBy(c => c.CreatedUtc)
            .Select(c => new TestCaseCommentView(c.Id, c.AuthorId, DisplayName(c.Author), c.Body.Trim(), c.CreatedUtc))
            .ToList();
    }

    public async Task<Guid> AddCommentAsync(Guid testCaseId, string authorId, string body, CancellationToken ct = default)
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
        return comment.Id;
    }

    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default)
    {
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
