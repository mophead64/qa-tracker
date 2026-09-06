using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Storage;

namespace QaTracker.Web.Attachments;

/// <summary>An attachment as shown to the UI, with the uploader's display name resolved.</summary>
public sealed record AttachmentView(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Description,
    DateTimeOffset UploadedUtc,
    string UploadedById,
    string UploadedByName);

/// <summary>An attachment's content, streamed back through the download proxy.</summary>
public sealed record AttachmentContent(Stream Content, string ContentType, string FileName);

/// <summary>
/// Reads and writes <see cref="Attachment"/> rows and their backing objects in
/// <see cref="IFileStorage"/>. Shared across all three owner kinds (project, test case,
/// defect) — see the type doc on <see cref="Attachment"/> for why one table.
/// </summary>
public sealed class AttachmentService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IFileStorage storage,
    TimeProvider timeProvider,
    ILogger<AttachmentService> logger)
{
    public Task<IReadOnlyList<AttachmentView>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) =>
        ListAsync(db => db.Attachments.Where(a => a.ProjectId == projectId), ct);

    public Task<IReadOnlyList<AttachmentView>> ListForTestCaseAsync(Guid testCaseId, CancellationToken ct = default) =>
        ListAsync(db => db.Attachments.Where(a => a.TestCaseId == testCaseId), ct);

    public Task<IReadOnlyList<AttachmentView>> ListForDefectAsync(Guid defectId, CancellationToken ct = default) =>
        ListAsync(db => db.Attachments.Where(a => a.DefectId == defectId), ct);

    private async Task<IReadOnlyList<AttachmentView>> ListAsync(
        Func<ApplicationDbContext, IQueryable<Attachment>> query, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await query(db)
            .AsNoTracking()
            .Include(a => a.UploadedBy)
            .ToListAsync(ct);

        return rows
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.UploadedUtc)
            .Select(ToView)
            .ToList();
    }

    public async Task<AttachmentView> UploadAsync(
        AttachmentOwner owner,
        Guid ownerId,
        Stream content,
        string fileName,
        string contentType,
        long sizeBytes,
        string? description,
        string uploadedById,
        CancellationToken ct = default)
    {
        var key = $"{owner.ToString().ToLowerInvariant()}/{ownerId}/{Guid.NewGuid()}{Path.GetExtension(fileName)}";
        await storage.PutAsync(key, content, contentType, ct);

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = key,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            UploadedUtc = timeProvider.GetUtcNow(),
            UploadedById = uploadedById,
        };

        switch (owner)
        {
            case AttachmentOwner.Project:
                attachment.ProjectId = ownerId;
                break;
            case AttachmentOwner.TestCase:
                attachment.TestCaseId = ownerId;
                break;
            case AttachmentOwner.Defect:
                attachment.DefectId = ownerId;
                break;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        attachment.SortOrder = await db.Attachments.CountAsync(OwnerPredicate(owner, ownerId), ct);
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return ToView(attachment);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null)
        {
            return;
        }

        await DeleteFromStorageAsync(attachment.StorageKey, ct);
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>QA can modify any attachment; everyone else only their own uploads.</summary>
    public async Task<bool> CanModifyAsync(Guid id, string userId, bool isQa, CancellationToken ct = default)
    {
        if (isQa)
        {
            await using var qaDb = await dbFactory.CreateDbContextAsync(ct);
            return await qaDb.Attachments.AnyAsync(a => a.Id == id, ct);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Attachments.AnyAsync(a => a.Id == id && a.UploadedById == userId, ct);
    }

    public async Task<AttachmentContent?> OpenAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(attachment.StorageKey, ct);
        return new AttachmentContent(stream, attachment.ContentType, attachment.FileName);
    }

    /// <summary>Deletes every attachment on a project (its storage objects, then the rows).</summary>
    public Task PurgeForProjectAsync(Guid projectId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(a => a.ProjectId == projectId), ct);

    /// <summary>Deletes every attachment on every test case under a scope.</summary>
    public Task PurgeForTestScopeAsync(Guid scopeId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(a => a.TestCase!.TestScopeId == scopeId), ct);

    public Task PurgeForTestCaseAsync(Guid testCaseId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(a => a.TestCaseId == testCaseId), ct);

    public Task PurgeForDefectAsync(Guid defectId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(a => a.DefectId == defectId), ct);

    private async Task PurgeAsync(Func<ApplicationDbContext, IQueryable<Attachment>> query, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var keys = await query(db).Select(a => a.StorageKey).ToListAsync(ct);
        foreach (var key in keys)
        {
            await DeleteFromStorageAsync(key, ct);
        }
        // Rows are removed by the caller's own delete of the owning aggregate (DB-level cascade).
    }

    private async Task DeleteFromStorageAsync(string key, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception ex)
        {
            // Best-effort: a storage hiccup shouldn't block deleting the record itself.
            logger.LogWarning(ex, "Failed to delete attachment object {Key} from storage.", key);
        }
    }

    private static System.Linq.Expressions.Expression<Func<Attachment, bool>> OwnerPredicate(
        AttachmentOwner owner, Guid ownerId) => owner switch
    {
        AttachmentOwner.Project => a => a.ProjectId == ownerId,
        AttachmentOwner.TestCase => a => a.TestCaseId == ownerId,
        AttachmentOwner.Defect => a => a.DefectId == ownerId,
        _ => throw new ArgumentOutOfRangeException(nameof(owner)),
    };

    private static AttachmentView ToView(Attachment a) => new(
        a.Id,
        a.FileName,
        a.ContentType,
        a.SizeBytes,
        a.Description,
        a.UploadedUtc,
        a.UploadedById,
        DisplayName(a.UploadedBy));

    private static string DisplayName(ApplicationUser? user)
    {
        if (user is null)
        {
            return "Unknown";
        }

        return !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.UserName ?? "Unknown";
    }
}
