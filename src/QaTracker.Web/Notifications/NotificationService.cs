using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;

namespace QaTracker.Web.Notifications;

/// <summary>One notification as shown in the bell dropdown, with just enough of its
/// defect resolved to link straight to it.</summary>
public sealed record NotificationView(
    Guid Id, string Message, DateTimeOffset CreatedUtc, Guid ProjectId, Guid DefectId, int DefectNumber);

/// <summary>
/// Creates and reads per-user notifications about defect events. Uses a context factory
/// so each call gets a short-lived context — mirrors <see cref="Dashboard.ProjectActionsService"/>
/// for reads and <see cref="DefectService"/> for writes.
/// </summary>
public sealed class NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider timeProvider)
{
    /// <summary>Active (not dismissed) notifications for a user, newest first.</summary>
    public async Task<IReadOnlyList<NotificationView>> ListActiveAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var active = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.DismissedUtc == null)
            .Include(n => n.Defect)
            .ToListAsync(ct);

        // Ordered client-side: SQLite (used by the unit tests) can't translate ORDER BY
        // over a DateTimeOffset column, and this list is small enough per user regardless.
        return active
            .OrderByDescending(n => n.CreatedUtc)
            .Select(n => new NotificationView(n.Id, n.Message, n.CreatedUtc, n.Defect!.ProjectId, n.DefectId, n.Defect!.Number))
            .ToList();
    }

    public async Task<int> CountActiveAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.CountAsync(n => n.UserId == userId && n.DismissedUtc == null, ct);
    }

    /// <summary>Dismisses one notification. A no-op if it doesn't belong to the user or is already dismissed.</summary>
    public async Task DismissAsync(Guid id, string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (notification is null || notification.DismissedUtc is not null)
        {
            return;
        }

        notification.DismissedUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Every Dev assigned to the defect's project, when a new defect is raised.</summary>
    public async Task NotifyNewDefectAsync(Defect defect, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var devIds = await ProjectMemberIdsInRoleAsync(db, defect.ProjectId, Roles.Dev, ct);
        await AddAsync(db, devIds, defect.Id, $"New defect {DefectDisplay.Ref(defect.Number)}: {defect.Summary}", ct);
    }

    /// <summary>The assignee, when their defect is moved back into Not fixed.</summary>
    public async Task NotifyReturnedToNotFixedAsync(Defect defect, CancellationToken ct = default)
    {
        if (defect.AssignedToId is null)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [defect.AssignedToId], defect.Id,
            $"{DefectDisplay.Ref(defect.Number)} was moved back to Not fixed: {defect.Summary}", ct);
    }

    /// <summary>The assignee, when the defect moves to To check — only if they're a QA.</summary>
    public async Task NotifyReadyToCheckAsync(Defect defect, CancellationToken ct = default)
    {
        if (defect.AssignedToId is null)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await IsInRoleAsync(db, defect.AssignedToId, Roles.QA, ct))
        {
            return;
        }

        await AddAsync(db, [defect.AssignedToId], defect.Id,
            $"{DefectDisplay.Ref(defect.Number)} is ready to check: {defect.Summary}", ct);
    }

    /// <summary>The new assignee, when a defect is assigned to them — only if they're a QA.</summary>
    public async Task NotifyAssignedAsync(Defect defect, string assigneeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await IsInRoleAsync(db, assigneeId, Roles.QA, ct))
        {
            return;
        }

        await AddAsync(db, [assigneeId], defect.Id,
            $"{DefectDisplay.Ref(defect.Number)} was assigned to you: {defect.Summary}", ct);
    }

    /// <summary>The Dev assignee, when a new comment is added — skips the comment's own author.</summary>
    public async Task NotifyCommentAsync(Defect defect, string authorId, CancellationToken ct = default)
    {
        if (defect.AssignedToId is null || defect.AssignedToId == authorId)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await IsInRoleAsync(db, defect.AssignedToId, Roles.Dev, ct))
        {
            return;
        }

        await AddAsync(db, [defect.AssignedToId], defect.Id,
            $"New comment on {DefectDisplay.Ref(defect.Number)}: {defect.Summary}", ct);
    }

    private async Task AddAsync(
        ApplicationDbContext db, IEnumerable<string> userIds, Guid defectId, string message, CancellationToken ct)
    {
        var recipients = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        db.Notifications.AddRange(recipients.Select(userId => new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DefectId = defectId,
            Message = message,
            CreatedUtc = now,
        }));
        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<string>> ProjectMemberIdsInRoleAsync(
        ApplicationDbContext db, Guid projectId, string role, CancellationToken ct)
    {
        var memberIds = await db.Projects
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Members)
            .Select(m => m.Id)
            .ToListAsync(ct);

        return await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where r.Name == role && memberIds.Contains(ur.UserId)
            select ur.UserId)
            .ToListAsync(ct);
    }

    private static async Task<bool> IsInRoleAsync(ApplicationDbContext db, string userId, string role, CancellationToken ct) =>
        await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId && r.Name == role
            select ur.UserId)
            .AnyAsync(ct);
}
