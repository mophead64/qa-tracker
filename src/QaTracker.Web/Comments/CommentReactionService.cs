using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;

namespace QaTracker.Web.Comments;

/// <summary>
/// Thumbs up / down on comments of every kind. Reactions are read alongside their comments
/// (each comment service includes them); this only handles a user reacting.
/// </summary>
public sealed class CommentReactionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider timeProvider,
    NotificationService notifications)
{
    /// <summary>
    /// Records <paramref name="userId"/>'s <paramref name="reaction"/> on a comment. A user
    /// holds at most one: the same reaction again takes it back, the other one replaces it.
    /// A new or switched reaction notifies the comment's author; taking one back doesn't.
    /// Returns false (and does nothing) when the comment doesn't exist.
    /// </summary>
    public async Task<bool> ReactAsync(
        CommentKind kind, Guid commentId, string userId, Reaction reaction, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await CommentExistsAsync(db, kind, commentId, ct))
        {
            return false;
        }

        var existing = await db.CommentReactions
            .Where(OnComment(kind, commentId))
            .FirstOrDefaultAsync(r => r.UserId == userId, ct);

        var takenBack = existing is not null && existing.Reaction == reaction;
        if (existing is null)
        {
            var added = new CommentReaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Reaction = reaction,
                CreatedUtc = timeProvider.GetUtcNow(),
            };
            switch (kind)
            {
                case CommentKind.Project:
                    added.ProjectCommentId = commentId;
                    break;
                case CommentKind.TestCase:
                    added.TestCaseCommentId = commentId;
                    break;
                case CommentKind.Defect:
                    added.DefectCommentId = commentId;
                    break;
            }
            db.CommentReactions.Add(added);
        }
        else if (takenBack)
        {
            db.CommentReactions.Remove(existing);
        }
        else
        {
            existing.Reaction = reaction;
            existing.CreatedUtc = timeProvider.GetUtcNow();
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A double-click raced its own first request (the per-user unique index caught the
            // second insert, or the row it meant to change is already gone). The first one won,
            // and it did any notifying.
            return true;
        }

        if (!takenBack)
        {
            await notifications.NotifyReactionAsync(kind, commentId, userId, reaction, ct);
        }

        return true;
    }

    private static Task<bool> CommentExistsAsync(
        ApplicationDbContext db, CommentKind kind, Guid commentId, CancellationToken ct) => kind switch
    {
        CommentKind.Project => db.ProjectComments.AnyAsync(c => c.Id == commentId, ct),
        CommentKind.TestCase => db.TestCaseComments.AnyAsync(c => c.Id == commentId, ct),
        CommentKind.Defect => db.DefectComments.AnyAsync(c => c.Id == commentId, ct),
        _ => Task.FromResult(false),
    };

    private static Expression<Func<CommentReaction, bool>> OnComment(CommentKind kind, Guid commentId) => kind switch
    {
        CommentKind.Project => r => r.ProjectCommentId == commentId,
        CommentKind.TestCase => r => r.TestCaseCommentId == commentId,
        CommentKind.Defect => r => r.DefectCommentId == commentId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
