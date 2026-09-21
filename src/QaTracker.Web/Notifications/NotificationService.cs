using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Notifications;

/// <summary>One notification as shown in the bell dropdown, with just enough resolved to
/// link straight to its defect or test case (<see cref="Path"/>) and name the project.</summary>
public sealed record NotificationView(
    Guid Id, string Message, DateTimeOffset CreatedUtc, Guid ProjectId, string ProjectName, string Path);

/// <summary>
/// Creates and reads per-user notifications about defect events. Uses a context factory
/// so each call gets a short-lived context — mirrors <see cref="Dashboard.ProjectActionsService"/>
/// for reads and <see cref="DefectService"/> for writes.
/// </summary>
public sealed class NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider timeProvider)
{
    /// <summary>Every notification for a user, newest first. Notifications persist until
    /// <see cref="ClearAllAsync"/> — read status doesn't remove them from this list.</summary>
    public async Task<IReadOnlyList<NotificationView>> ListAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var all = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .Include(n => n.Defect!).ThenInclude(d => d.Project)
            .Include(n => n.TestCase!).ThenInclude(tc => tc.TestScope!).ThenInclude(s => s.Project)
            .ToListAsync(ct);

        // Ordered client-side: SQLite (used by the unit tests) can't translate ORDER BY
        // over a DateTimeOffset column, and this list is small enough per user regardless.
        return all
            .OrderByDescending(n => n.CreatedUtc)
            .Select(ToView)
            .ToList();
    }

    private static NotificationView ToView(Notification n)
    {
        if (n.TestCase is { TestScope: { } scope })
        {
            return new NotificationView(
                n.Id, n.Message, n.CreatedUtc, scope.ProjectId, scope.Project?.Name ?? "",
                $"projects/{scope.ProjectId}/test-cases/scopes/{scope.Id}/cases/{n.TestCase.Id}");
        }

        var defect = n.Defect!;
        return new NotificationView(
            n.Id, n.Message, n.CreatedUtc, defect.ProjectId, defect.Project?.Name ?? "",
            $"projects/{defect.ProjectId}/defects/{defect.Id}");
    }

    /// <summary>Unread count for the bell badge.</summary>
    public async Task<int> CountUnreadAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadUtc == null, ct);
    }

    /// <summary>Marks every unread notification read (fired when the bell dropdown is opened).
    /// Notifications stay in the list — this only affects the unread badge count. Returns the
    /// count marked.</summary>
    public async Task<int> MarkAllReadAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications
            .Where(n => n.UserId == userId && n.ReadUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadUtc, now), ct);
    }

    /// <summary>Permanently deletes one notification, if it belongs to the user. A no-op
    /// (no throw) if it doesn't exist or belongs to someone else.</summary>
    public async Task ClearAsync(Guid id, string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Permanently deletes every notification for a user ("Clear all"). Returns the
    /// count deleted.</summary>
    public async Task<int> ClearAllAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return 0;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications
            .Where(n => n.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Every Dev assigned to the defect's project, when a new defect is raised.
    /// <paramref name="actingUserId"/> (the person who raised it) never gets notified.</summary>
    public async Task NotifyNewDefectAsync(Defect defect, string? actingUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var devIds = await ProjectMemberIdsInRoleAsync(db, defect.ProjectId, Roles.Dev, ct);
        await AddAsync(db, devIds, defect.Id, $"New defect {DefectDisplay.Ref(defect.Number)}: {defect.Summary}", actingUserId, ct);
    }

    /// <summary>The assignee, when their defect is moved back into Not fixed — unless they
    /// moved it themselves (<paramref name="actingUserId"/>).</summary>
    public async Task NotifyReturnedToNotFixedAsync(Defect defect, string? actingUserId, CancellationToken ct = default)
    {
        if (defect.AssignedToId is null)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [defect.AssignedToId], defect.Id,
            $"{DefectDisplay.Ref(defect.Number)} was moved back to Not fixed: {defect.Summary}", actingUserId, ct);
    }

    /// <summary>The assignee, when the defect moves to To check — only if they're a QA, and
    /// not if they moved it themselves (<paramref name="actingUserId"/>).</summary>
    public async Task NotifyReadyToCheckAsync(Defect defect, string? actingUserId, CancellationToken ct = default)
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
            $"{DefectDisplay.Ref(defect.Number)} is ready to check: {defect.Summary}", actingUserId, ct);
    }

    /// <summary>The new assignee, when a defect is assigned to them — only if they're a QA,
    /// and not when they assigned it to themselves (<paramref name="actingUserId"/>).</summary>
    public async Task NotifyAssignedAsync(Defect defect, string assigneeId, string? actingUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await IsInRoleAsync(db, assigneeId, Roles.QA, ct))
        {
            return;
        }

        await AddAsync(db, [assigneeId], defect.Id,
            $"{DefectDisplay.Ref(defect.Number)} was assigned to you: {defect.Summary}", actingUserId, ct);
    }

    /// <summary>The Dev assignee, when a new comment is added — skips the comment's own author.</summary>
    public async Task NotifyCommentAsync(
        Defect defect, string authorId, IReadOnlyCollection<string>? mentionedIds = null, CancellationToken ct = default)
    {
        // A mentioned assignee gets the (more specific) mention notification instead.
        if (defect.AssignedToId is null || defect.AssignedToId == authorId
            || mentionedIds?.Contains(defect.AssignedToId) == true)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await IsInRoleAsync(db, defect.AssignedToId, Roles.Dev, ct))
        {
            return;
        }

        await AddAsync(db, [defect.AssignedToId], defect.Id,
            $"New comment on {DefectDisplay.Ref(defect.Number)}: {defect.Summary}", authorId, ct);
    }

    /// <summary>
    /// Tells everyone @-mentioned in a comment. <paramref name="candidateIds"/> is what the
    /// comment form posted; each is kept only if they're on the project's team, aren't the
    /// author, and their "@Name" is still in <paramref name="body"/> (so a mention deleted
    /// from the text after picking doesn't notify). Returns the ids actually notified.
    /// Exactly one of <paramref name="defect"/> / <paramref name="testCase"/> is the subject.
    /// </summary>
    public async Task<IReadOnlyList<string>> NotifyMentionedAsync(
        Guid projectId, Defect? defect, TestCase? testCase, string authorId, string body,
        IReadOnlyCollection<string>? candidateIds, CancellationToken ct = default)
    {
        if (candidateIds is null || candidateIds.Count == 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var members = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Members)
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(ct);

        var mentioned = members
            .Where(m => m.Id != authorId && candidateIds.Contains(m.Id))
            .Where(m => body.Contains("@" + MentionName(m.FullName, m.UserName), StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Id)
            .ToList();
        if (mentioned.Count == 0)
        {
            return [];
        }

        var author = await db.Users.AsNoTracking()
            .Where(u => u.Id == authorId)
            .Select(u => new { u.FullName, u.UserName })
            .FirstOrDefaultAsync(ct);
        var who = author is null ? "Someone" : MentionName(author.FullName, author.UserName);

        string message;
        if (defect is not null)
        {
            message = $"{who} mentioned you in a comment on {DefectDisplay.Ref(defect.Number)}: {defect.Summary}";
        }
        else
        {
            var scenario = testCase!.Scenario.ReplaceLineEndings(" ").Trim();
            message = $"{who} mentioned you in a comment on a test case: {scenario}";
        }

        await AddAsync(db, mentioned, defect?.Id, testCase?.Id, Truncate(message, 500), authorId, ct);
        return mentioned;
    }

    /// <summary>The text a mention of this user is written as ("@" + this) — the same
    /// display name the mention picker offers.</summary>
    public static string MentionName(string? fullName, string? userName) =>
        !string.IsNullOrWhiteSpace(fullName) ? fullName : userName ?? "Unknown";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private Task AddAsync(
        ApplicationDbContext db, IEnumerable<string> userIds, Guid defectId, string message,
        string? actingUserId, CancellationToken ct) =>
        AddAsync(db, userIds, defectId, null, message, actingUserId, ct);

    private async Task AddAsync(
        ApplicationDbContext db, IEnumerable<string> userIds, Guid? defectId, Guid? testCaseId, string message,
        string? actingUserId, CancellationToken ct)
    {
        // The person who triggered the event never needs telling about their own action.
        var recipients = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && id != actingUserId)
            .Distinct()
            .ToList();
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
            TestCaseId = testCaseId,
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
