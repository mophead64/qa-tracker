using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;

namespace QaTracker.Web.Defects;

/// <summary>Editable fields of one evidence row.</summary>
public sealed record DefectEvidenceInput(string Description, string? Url);

/// <summary>Editable fields of a defect (status is set separately, like a test result).</summary>
public sealed record DefectInput(
    string Summary,
    string? ReproSteps,
    string? ExpectedResults,
    string? ActualResults,
    DefectSeverity Severity,
    string? AssignedToId,
    IReadOnlyList<DefectEvidenceInput> Evidence);

/// <summary>A comment on a defect, with its author's display name resolved.</summary>
public sealed record DefectCommentView(Guid Id, string AuthorId, string AuthorName, string Body, DateTimeOffset CreatedUtc);

/// <summary>
/// Roll-up of a project's defects for the dashboard. <see cref="Total"/> excludes
/// defects dismissed as <see cref="DefectStatus.NotADefect"/> — by QA/dev agreement
/// those aren't bugs, so they shouldn't inflate the KPI. <see cref="Open"/> is a subset
/// of <see cref="Total"/> that further excludes <see cref="DefectStatus.Fixed"/>.
/// </summary>
public sealed record DefectSummary(int Total, int Open);

/// <summary>
/// Reads and writes <see cref="Defect"/> aggregates. Uses a context factory so each call
/// gets a short-lived context (safe under Blazor Server circuits). Mirrors
/// <see cref="TestCases.TestCaseService"/>.
/// </summary>
public sealed class DefectService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider timeProvider,
    ProjectService projects,
    AttachmentService attachments)
{
    /// <summary>Defects in a project, most severe first then by number. Includes linked test cases.</summary>
    public async Task<IReadOnlyList<Defect>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.Defects
            .AsNoTracking()
            .Include(d => d.AssignedTo)
            .Include(d => d.TestCases)
            .Where(d => d.ProjectId == projectId)
            .ToListAsync(ct);

        return Ordered(list);
    }

    /// <summary>Defects linked to a given test case, most severe first then by number.</summary>
    public async Task<IReadOnlyList<Defect>> ListForTestCaseAsync(Guid testCaseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.Defects
            .AsNoTracking()
            .Where(d => d.TestCases.Any(tc => tc.Id == testCaseId))
            .ToListAsync(ct);

        return Ordered(list);
    }

    /// <summary>Most severe first, then ascending by per-project number.</summary>
    private static IReadOnlyList<Defect> Ordered(IEnumerable<Defect> defects) =>
        defects
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Number)
            .ToList();

    public async Task<Defect?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects
            .AsNoTracking()
            .Include(d => d.Evidence)
            .Include(d => d.AssignedTo)
            .Include(d => d.TestCases)
                .ThenInclude(tc => tc.TestScope)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (defect is not null)
        {
            defect.Evidence.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            defect.TestCases.Sort((a, b) => string.Compare(a.Scenario, b.Scenario, StringComparison.OrdinalIgnoreCase));
        }

        return defect;
    }

    public async Task<Defect> CreateAsync(
        Guid projectId,
        DefectInput input,
        string createdById,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var maxNumber = await db.Defects
                .Where(d => d.ProjectId == projectId)
                .MaxAsync(d => (int?)d.Number, ct) ?? 0;

            var defect = new Defect
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Number = maxNumber + 1,
                Summary = input.Summary.Trim(),
                ReproSteps = Normalize(input.ReproSteps),
                ExpectedResults = Normalize(input.ExpectedResults),
                ActualResults = Normalize(input.ActualResults),
                Severity = input.Severity,
                Status = DefectStatus.NotFixed,
                AssignedToId = string.IsNullOrWhiteSpace(input.AssignedToId) ? null : input.AssignedToId,
                CreatedById = createdById,
                CreatedUtc = now,
                UpdatedUtc = now,
                Evidence = BuildEvidence(input.Evidence),
            };

            db.Defects.Add(defect);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                continue; // lost the race for this Number — recompute and retry
            }

            // First item in the project moves it from "Not started" to "In flight".
            await projects.MarkInFlightAsync(projectId, ct);
            return defect;
        }

        throw new InvalidOperationException($"Could not allocate a defect number for project {projectId}.");
    }

    /// <summary>Updates the editable fields and replaces the evidence set. Does not touch status.</summary>
    public async Task UpdateAsync(Guid id, DefectInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        defect.Summary = input.Summary.Trim();
        defect.ReproSteps = Normalize(input.ReproSteps);
        defect.ExpectedResults = Normalize(input.ExpectedResults);
        defect.ActualResults = Normalize(input.ActualResults);
        defect.Severity = input.Severity;
        defect.AssignedToId = string.IsNullOrWhiteSpace(input.AssignedToId) ? null : input.AssignedToId;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        // Replace the evidence set wholesale — simplest correct behaviour for a handful of rows.
        await db.DefectEvidence.Where(e => e.DefectId == id).ExecuteDeleteAsync(ct);
        var replacement = BuildEvidence(input.Evidence);
        replacement.ForEach(e => e.DefectId = id);
        db.DefectEvidence.AddRange(replacement);

        await db.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(Guid id, DefectStatus status, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        defect.Status = status;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    public async Task SetAssigneeAsync(Guid id, string? assigneeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        defect.AssignedToId = string.IsNullOrWhiteSpace(assigneeId) ? null : assigneeId;
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Links a test case to the defect. Idempotent.</summary>
    public async Task LinkTestCaseAsync(Guid id, Guid testCaseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects
            .Include(d => d.TestCases)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        if (defect.TestCases.Any(tc => tc.Id == testCaseId))
        {
            return;
        }

        var testCase = await db.TestCases.FirstOrDefaultAsync(tc => tc.Id == testCaseId, ct)
            ?? throw new InvalidOperationException($"Test case {testCaseId} not found.");

        defect.TestCases.Add(testCase);
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes one test-case link from the defect. Idempotent.</summary>
    public async Task UnlinkTestCaseAsync(Guid id, Guid testCaseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects
            .Include(d => d.TestCases)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Defect {id} not found.");

        var link = defect.TestCases.FirstOrDefault(tc => tc.Id == testCaseId);
        if (link is null)
        {
            return;
        }

        defect.TestCases.Remove(link);
        defect.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await attachments.PurgeForDefectAsync(id, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Defects.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<DefectCommentView>> ListCommentsAsync(Guid defectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var comments = await db.DefectComments
            .AsNoTracking()
            .Where(c => c.DefectId == defectId)
            .Include(c => c.Author)
            .ToListAsync(ct);

        return comments
            .OrderBy(c => c.CreatedUtc)
            .Select(c => new DefectCommentView(c.Id, c.AuthorId, DisplayName(c.Author), c.Body.Trim(), c.CreatedUtc))
            .ToList();
    }

    public async Task<Guid> AddCommentAsync(Guid defectId, string authorId, string body, CancellationToken ct = default)
    {
        var comment = new DefectComment
        {
            Id = Guid.NewGuid(),
            DefectId = defectId,
            AuthorId = authorId,
            Body = body.Trim(),
            CreatedUtc = timeProvider.GetUtcNow(),
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.DefectComments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment.Id;
    }

    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.DefectComments.Where(c => c.Id == commentId).ExecuteDeleteAsync(ct);
    }

    public async Task<DefectSummary> SummariseProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var total = await db.Defects.CountAsync(
            d => d.ProjectId == projectId && d.Status != DefectStatus.NotADefect, ct);
        var open = await db.Defects.CountAsync(
            d => d.ProjectId == projectId
                && d.Status != DefectStatus.Fixed
                && d.Status != DefectStatus.NotADefect, ct);

        return new DefectSummary(total, open);
    }

    private static List<DefectEvidence> BuildEvidence(IReadOnlyList<DefectEvidenceInput> rows)
    {
        var result = new List<DefectEvidence>();
        var order = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Description))
            {
                continue;
            }

            result.Add(new DefectEvidence
            {
                Id = Guid.NewGuid(),
                Description = row.Description.Trim(),
                Url = Normalize(row.Url),
                SortOrder = order++,
            });
        }

        return result;
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
