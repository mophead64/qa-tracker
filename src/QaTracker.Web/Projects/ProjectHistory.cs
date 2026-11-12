using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.Projects;

/// <summary>Adds <see cref="ProjectEvent"/> rows to a context; the caller's SaveChanges commits them
/// with the change they describe.</summary>
public static class ProjectHistory
{
    public static void Record(
        ApplicationDbContext db,
        Guid projectId,
        ProjectEventKind kind,
        string? actorId,
        DateTimeOffset occurredUtc,
        string? fromValue = null,
        string? toValue = null,
        Guid? subjectId = null)
    {
        db.ProjectEvents.Add(new ProjectEvent
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Kind = kind,
            ActorId = string.IsNullOrEmpty(actorId) ? null : actorId,
            OccurredUtc = occurredUtc,
            SubjectId = subjectId,
            FromValue = Truncate(fromValue),
            ToValue = Truncate(toValue),
        });
    }

    /// <summary>A user's display name as history snapshots it (survives the account being deleted).</summary>
    public static string DisplayName(ApplicationUser? user) =>
        user is null ? "Unknown"
        : !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName
        : user.UserName ?? "Unknown";

    /// <summary>Looks up a user's display name for a snapshot; null when there's no such user.</summary>
    public static async Task<string?> DisplayNameAsync(ApplicationDbContext db, string? userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : DisplayName(user);
    }

    private static string? Truncate(string? value) =>
        value is { Length: > 500 } ? value[..500] : value;
}
