using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.Storage;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Attachments;

/// <summary>An attachment as shown to the UI, with the uploader's display name resolved.
/// <paramref name="Comment"/> is set when the file came with a comment on the item rather than
/// being added to the item's own attachments.</summary>
public sealed record AttachmentView(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Description,
    DateTimeOffset UploadedUtc,
    string UploadedById,
    string UploadedByName,
    AttachmentComment? Comment = null);

/// <summary>The comment a file was attached to: who wrote it, when, what it says, and which page of
/// the item's comment thread it's on (threads page oldest first; see <see cref="Comments.CommentPaging"/>).</summary>
public sealed record AttachmentComment(Guid Id, string AuthorName, DateTimeOffset CreatedUtc, string Body, int ThreadPage);

/// <summary>A file attached to a comment, as shown under the comment.</summary>
public sealed record CommentAttachmentView(Guid Id, string FileName, long SizeBytes)
{
    public static CommentAttachmentView From(Attachment a) => new(a.Id, a.FileName, a.SizeBytes);
}

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
    /// <summary>Whether an object-storage backend is configured (uploads are possible).</summary>
    public bool StorageConfigured => storage.IsConfigured;

    /// <summary>The project's attachments, then the files on its dashboard comments.</summary>
    public Task<IReadOnlyList<AttachmentView>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) =>
        ListAsync(
            db => db.Attachments.Where(a => a.ProjectId == projectId),
            db => db.Attachments.Where(a => a.ProjectComment!.ProjectId == projectId)
                .Select(a => new CommentFileRow(a, a.UploadedBy, a.ProjectComment!.Id, a.ProjectComment.CreatedUtc,
                    a.ProjectComment.Body, a.ProjectComment.Author)),
            db => db.ProjectComments.Where(c => c.ProjectId == projectId).Select(c => new ThreadRow(c.Id, c.CreatedUtc)),
            ct);

    /// <summary>The test case's attachments, then the files on its comments.</summary>
    public Task<IReadOnlyList<AttachmentView>> ListForTestCaseAsync(Guid testCaseId, CancellationToken ct = default) =>
        ListAsync(
            db => db.Attachments.Where(a => a.TestCaseId == testCaseId),
            db => db.Attachments.Where(a => a.TestCaseComment!.TestCaseId == testCaseId)
                .Select(a => new CommentFileRow(a, a.UploadedBy, a.TestCaseComment!.Id, a.TestCaseComment.CreatedUtc,
                    a.TestCaseComment.Body, a.TestCaseComment.Author)),
            db => db.TestCaseComments.Where(c => c.TestCaseId == testCaseId).Select(c => new ThreadRow(c.Id, c.CreatedUtc)),
            ct);

    /// <summary>The defect's attachments, then the files on its comments.</summary>
    public Task<IReadOnlyList<AttachmentView>> ListForDefectAsync(Guid defectId, CancellationToken ct = default) =>
        ListAsync(
            db => db.Attachments.Where(a => a.DefectId == defectId),
            db => db.Attachments.Where(a => a.DefectComment!.DefectId == defectId)
                .Select(a => new CommentFileRow(a, a.UploadedBy, a.DefectComment!.Id, a.DefectComment.CreatedUtc,
                    a.DefectComment.Body, a.DefectComment.Author)),
            db => db.DefectComments.Where(c => c.DefectId == defectId).Select(c => new ThreadRow(c.Id, c.CreatedUtc)),
            ct);

    private sealed record CommentFileRow(
        Attachment Attachment, ApplicationUser? UploadedBy, Guid CommentId, DateTimeOffset CommentCreatedUtc,
        string CommentBody, ApplicationUser? CommentAuthor);

    private sealed record ThreadRow(Guid Id, DateTimeOffset CreatedUtc);

    /// <summary>The owner's own files in their set order, then its comments' files oldest first.</summary>
    private async Task<IReadOnlyList<AttachmentView>> ListAsync(
        Func<ApplicationDbContext, IQueryable<Attachment>> own,
        Func<ApplicationDbContext, IQueryable<CommentFileRow>> onComments,
        Func<ApplicationDbContext, IQueryable<ThreadRow>> thread,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await own(db)
            .AsNoTracking()
            .Include(a => a.UploadedBy)
            .ToListAsync(ct);
        var commentFiles = await onComments(db).AsNoTracking().ToListAsync(ct);

        var views = rows
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.UploadedUtc)
            .Select(ToView)
            .ToList();
        if (commentFiles.Count == 0)
        {
            return views;
        }

        // Which page of the thread each comment sits on, so the list can link straight to it.
        var pages = (await thread(db).AsNoTracking().ToListAsync(ct))
            .OrderBy(c => c.CreatedUtc)
            .Select((c, index) => (c.Id, Page: index / Comments.CommentPaging.PageSize + 1))
            .ToDictionary(c => c.Id, c => c.Page);

        views.AddRange(commentFiles
            .OrderBy(r => r.Attachment.UploadedUtc)
            .Select(r =>
            {
                r.Attachment.UploadedBy = r.UploadedBy;
                return ToView(r.Attachment) with
                {
                    Comment = new AttachmentComment(r.CommentId, DisplayName(r.CommentAuthor), r.CommentCreatedUtc,
                        r.CommentBody.Trim(), pages.GetValueOrDefault(r.CommentId, 1)),
                };
            }));
        return views;
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
            case AttachmentOwner.TestCaseComment:
                attachment.TestCaseCommentId = ownerId;
                break;
            case AttachmentOwner.DefectComment:
                attachment.DefectCommentId = ownerId;
                break;
            case AttachmentOwner.ProjectComment:
                attachment.ProjectCommentId = ownerId;
                break;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        attachment.SortOrder = await db.Attachments.CountAsync(OwnerPredicate(owner, ownerId), ct);
        db.Attachments.Add(attachment);
        if (attachment.TestCaseId is { } testCaseId)
        {
            TestCaseHistory.Record(db, testCaseId, TestCaseEventKind.FileAdded, uploadedById,
                attachment.UploadedUtc, toValue: attachment.FileName);
        }
        else if (attachment.DefectId is { } defectId)
        {
            DefectHistory.Record(db, defectId, DefectEventKind.FileAdded, uploadedById,
                attachment.UploadedUtc, toValue: attachment.FileName);
        }
        else if (attachment.ProjectId is { } projectId)
        {
            ProjectHistory.Record(db, projectId, ProjectEventKind.FileAdded, uploadedById,
                attachment.UploadedUtc, toValue: attachment.FileName);
        }

        await db.SaveChangesAsync(ct);

        return ToView(attachment);
    }

    /// <summary>Deletes one attachment. Removing a test case's or defect's file is recorded in its history as
    /// <paramref name="deletedById"/>.</summary>
    public async Task DeleteAsync(Guid id, string? deletedById = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null)
        {
            return;
        }

        await DeleteFromStorageAsync(attachment.StorageKey, ct);
        db.Attachments.Remove(attachment);
        if (attachment.TestCaseId is { } testCaseId)
        {
            TestCaseHistory.Record(db, testCaseId, TestCaseEventKind.FileRemoved, deletedById,
                timeProvider.GetUtcNow(), fromValue: attachment.FileName);
        }
        else if (attachment.DefectId is { } defectId)
        {
            DefectHistory.Record(db, defectId, DefectEventKind.FileRemoved, deletedById,
                timeProvider.GetUtcNow(), fromValue: attachment.FileName);
        }
        else if (attachment.ProjectId is { } projectId)
        {
            ProjectHistory.Record(db, projectId, ProjectEventKind.FileRemoved, deletedById,
                timeProvider.GetUtcNow(), fromValue: attachment.FileName);
        }

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

    /// <summary>
    /// Deletes every attachment in a project — its own, its comments', and those on its test
    /// cases, defects and their comments (storage objects first; the rows go with the project
    /// via cascade).
    /// </summary>
    public Task PurgeForProjectAsync(Guid projectId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(a =>
            a.ProjectId == projectId
            || a.ProjectComment!.ProjectId == projectId
            || a.TestCase!.TestScope!.ProjectId == projectId
            || a.Defect!.ProjectId == projectId
            || a.TestCaseComment!.TestCase!.TestScope!.ProjectId == projectId
            || a.DefectComment!.Defect!.ProjectId == projectId), ct);

    /// <summary>Deletes every attachment on every test case under a scope (and on their comments).</summary>
    public Task PurgeForTestScopeAsync(Guid scopeId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(a =>
            a.TestCase!.TestScopeId == scopeId
            || a.TestCaseComment!.TestCase!.TestScopeId == scopeId), ct);

    public Task PurgeForTestCaseAsync(Guid testCaseId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(a =>
            a.TestCaseId == testCaseId || a.TestCaseComment!.TestCaseId == testCaseId), ct);

    public Task PurgeForDefectAsync(Guid defectId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(a =>
            a.DefectId == defectId || a.DefectComment!.DefectId == defectId), ct);

    /// <summary>Deletes the file(s) attached to one comment (call before deleting the comment).</summary>
    public Task PurgeForCommentAsync(AttachmentOwner owner, Guid commentId, CancellationToken ct = default) =>
        PurgeAsync(db => db.Attachments.Where(OwnerPredicate(owner, commentId)), ct);

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
        AttachmentOwner.TestCaseComment => a => a.TestCaseCommentId == ownerId,
        AttachmentOwner.DefectComment => a => a.DefectCommentId == ownerId,
        AttachmentOwner.ProjectComment => a => a.ProjectCommentId == ownerId,
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
