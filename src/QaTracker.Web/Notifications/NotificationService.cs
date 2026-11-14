using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Notifications;

/// <summary>One notification as shown in the bell dropdown, with just enough resolved to
/// link straight to its defect, test case or project (<see cref="Path"/>) and name the project.</summary>
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
            .Include(n => n.Project)
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
        if (n.Project is { } project)
        {
            return new NotificationView(
                n.Id, n.Message, n.CreatedUtc, project.Id, project.Name, $"projects/{project.Id}#comments");
        }

        if (n.TestCase is { TestScope: { } scope })
        {
            return new NotificationView(
                n.Id, n.Message, n.CreatedUtc, scope.ProjectId, scope.Project?.Name ?? "",
                AppUrls.TestCase(scope.ProjectId, n.TestCase.Id));
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

    // ---- Defects ---------------------------------------------------------------------------
    // Each trigger names its NotificationTrigger; AddAsync then keeps only the people who want it
    // (their Settings choices, else the default) and never tells anyone about their own action.

    /// <summary>The project's team, when a new defect is raised (by default only developers want this).</summary>
    public async Task NotifyNewDefectAsync(Defect defect, string? actingUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var teamIds = await db.Projects
            .Where(p => p.Id == defect.ProjectId)
            .SelectMany(p => p.Members)
            .Select(m => m.Id)
            .ToListAsync(ct);
        await AddAsync(db, teamIds, NotificationTrigger.DefectRaisedOnMyProject, defect.Id,
            $"New defect {DefectDisplay.Ref(defect.Number)}: {defect.Summary}", actingUserId, ct);
    }

    /// <summary>The new assignee, when a defect is assigned to them by someone else.</summary>
    public async Task NotifyAssignedAsync(Defect defect, string assigneeId, string? actingUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [assigneeId], NotificationTrigger.DefectAssignedToMe, defect.Id,
            $"{DefectDisplay.Ref(defect.Number)} was assigned to you: {defect.Summary}", actingUserId, ct);
    }

    /// <summary>The previous assignee, when someone else reassigns or unassigns their defect.</summary>
    public async Task NotifyUnassignedAsync(Defect defect, string previousAssigneeId, string? actingUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [previousAssigneeId], NotificationTrigger.DefectUnassignedFromMe, defect.Id,
            $"{DefectDisplay.Ref(defect.Number)} is no longer assigned to you: {defect.Summary}", actingUserId, ct);
    }

    /// <summary>The assignee, when someone else changes their defect's status — "ready to check"
    /// and "back to Not fixed" worded as such.</summary>
    public async Task NotifyStatusChangedAsync(Defect defect, string? actingUserId, CancellationToken ct = default)
    {
        if (defect.AssignedToId is null)
        {
            return;
        }

        var reference = DefectDisplay.Ref(defect.Number);
        var message = defect.Status switch
        {
            DefectStatus.ToCheck => $"{reference} is ready to check: {defect.Summary}",
            DefectStatus.NotFixed => $"{reference} was moved back to Not fixed: {defect.Summary}",
            var status => $"{reference} was moved to {DefectDisplay.StatusLabel(status)}: {defect.Summary}",
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [defect.AssignedToId], NotificationTrigger.DefectStatusChanged, defect.Id, message, actingUserId, ct);
    }

    /// <summary>The assignee, when someone else edits their defect — <paramref name="what"/> says
    /// what changed ("summary and repro steps", "severity to High").</summary>
    public async Task NotifyEditedAsync(Defect defect, string what, string? actingUserId, CancellationToken ct = default)
    {
        if (defect.AssignedToId is null)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [defect.AssignedToId], NotificationTrigger.DefectEdited, defect.Id,
            Truncate($"{DefectDisplay.Ref(defect.Number)} was edited ({what}): {defect.Summary}", 500), actingUserId, ct);
    }

    /// <summary>The assignee, when someone else adds a file to their defect. Files go up one per
    /// request, so a batch only leaves one unread notification.</summary>
    public async Task NotifyDefectFileAddedAsync(Guid defectId, string? uploaderId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defect = await db.Defects.AsNoTracking()
            .Where(d => d.Id == defectId)
            .Select(d => new { d.AssignedToId, d.Number, d.Summary })
            .FirstOrDefaultAsync(ct);
        if (defect?.AssignedToId is null)
        {
            return;
        }

        var message = Truncate($"A file was added to {DefectDisplay.Ref(defect.Number)}: {defect.Summary}", 500);
        if (await HasUnreadAsync(db, defect.AssignedToId, message, defectId, null, null, ct))
        {
            return;
        }

        await AddAsync(db, [defect.AssignedToId], NotificationTrigger.DefectFileAdded, defectId, message, uploaderId, ct);
    }

    /// <summary>The assignee, when someone else comments on their defect — unless the comment
    /// mentions them (they get the mention instead).</summary>
    public async Task NotifyCommentAsync(
        Defect defect, string authorId, IReadOnlyCollection<string>? mentionedIds = null, CancellationToken ct = default)
    {
        if (defect.AssignedToId is null || mentionedIds?.Contains(defect.AssignedToId) == true)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [defect.AssignedToId], NotificationTrigger.DefectCommented, defect.Id,
            $"New comment on {DefectDisplay.Ref(defect.Number)}: {defect.Summary}", authorId, ct);
    }

    // ---- Test cases ------------------------------------------------------------------------

    /// <summary>Whoever last set the case's result, when someone else comments on it — unless
    /// the comment mentions them.</summary>
    public async Task NotifyTestCaseCommentAsync(
        TestCase testCase, string authorId, IReadOnlyCollection<string>? mentionedIds = null, CancellationToken ct = default)
    {
        if (testCase.TestedById is not { } testerId || mentionedIds?.Contains(testerId) == true)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [testerId], NotificationTrigger.TestCaseCommented, null, testCase.Id, null,
            Truncate($"New comment on {TestCaseDisplay.CaseLabel(testCase)}, a test case you tested", 500), authorId, ct);
    }

    /// <summary>Whoever had set the case's result, when someone else changes it.</summary>
    public async Task NotifyTestCaseResultChangedAsync(
        TestCase testCase, string previousTesterId, TestResult from, string? actingUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var message = $"{TestCaseDisplay.CaseLabel(testCase)}, a test case you tested, is now "
            + $"{TestCaseDisplay.ResultLabel(testCase.Result)} (was {TestCaseDisplay.ResultLabel(from)})";
        await AddAsync(db, [previousTesterId], NotificationTrigger.TestCaseResultChanged, null, testCase.Id, null,
            Truncate(message, 500), actingUserId, ct);
    }

    // ---- Projects --------------------------------------------------------------------------

    /// <summary>People someone else has just added to a project's team.</summary>
    public async Task NotifyAddedToTeamAsync(
        Guid projectId, string projectName, IEnumerable<string> userIds, string? actingUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, userIds, NotificationTrigger.AddedToProjectTeam, null, null, projectId,
            Truncate($"You were added to the team for {projectName}", 500), actingUserId, ct);
    }

    /// <summary>Someone another person has just taken off a project's team.</summary>
    public async Task NotifyRemovedFromTeamAsync(
        Guid projectId, string projectName, string userId, string? actingUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AddAsync(db, [userId], NotificationTrigger.RemovedFromProjectTeam, null, null, projectId,
            Truncate($"You were removed from the team for {projectName}", 500), actingUserId, ct);
    }

    // ---- Comments --------------------------------------------------------------------------

    /// <summary>
    /// Tells everyone @-mentioned in a comment. <paramref name="candidateIds"/> is what the
    /// comment form posted; each is kept only if they can be mentioned there (on the project's team,
    /// or a Viewer — see <see cref="UserDirectory.ListMentionableAsync"/>), aren't the author, and their "@Name" is still in <paramref name="body"/> (so a mention deleted
    /// from the text after picking doesn't notify). Returns the ids actually notified.
    /// The subject is <paramref name="defect"/> or <paramref name="testCase"/> when one is
    /// given, else the project itself (a comment on the project dashboard).
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
        var viewerIds = UserDirectory.ViewerIds(db);
        var viewers = await db.Users
            .AsNoTracking()
            .Where(u => u.DeletedUtc == null && viewerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(ct);

        var mentioned = members.Concat(viewers).DistinctBy(m => m.Id)
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
        else if (testCase is not null)
        {
            message = $"{who} mentioned you in a comment on test case {TestCaseDisplay.CaseLabel(testCase)}";
        }
        else
        {
            // The bell already shows which project, so the message needn't repeat its name.
            message = $"{who} mentioned you in a comment on the project dashboard";
        }

        var aboutProject = defect is null && testCase is null ? projectId : (Guid?)null;
        // Only those who want mention notifications count as told; anyone who's switched them off
        // can still hear about the comment through the defect / test case triggers.
        return await AddAsync(db, mentioned, NotificationTrigger.Mentioned, defect?.Id, testCase?.Id, aboutProject,
            Truncate(message, 500), authorId, ct);
    }

    /// <summary>
    /// The comment's author, when someone else gives it a thumbs up or down (the reactor never
    /// hears about their own reaction, including on their own comment). Skipped while the author
    /// still has the identical notification unread, so someone toggling a reaction off and on
    /// again doesn't stack up copies. Links to the defect, test case or project the comment is on.
    /// </summary>
    public async Task NotifyReactionAsync(
        CommentKind kind, Guid commentId, string reactorId, Reaction reaction, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        string? authorId;
        Guid? defectId = null, testCaseId = null, projectId = null;
        string where;
        switch (kind)
        {
            case CommentKind.Defect:
                var onDefect = await db.DefectComments.AsNoTracking()
                    .Where(c => c.Id == commentId)
                    .Select(c => new { c.AuthorId, c.DefectId, c.Defect!.Number, c.Defect.Summary })
                    .FirstOrDefaultAsync(ct);
                authorId = onDefect?.AuthorId;
                defectId = onDefect?.DefectId;
                where = onDefect is null ? "" : $"on {DefectDisplay.Ref(onDefect.Number)}: {onDefect.Summary}";
                break;
            case CommentKind.TestCase:
                var onCase = await db.TestCaseComments.AsNoTracking()
                    .Where(c => c.Id == commentId)
                    .Select(c => new { c.AuthorId, c.TestCaseId, c.TestCase!.Scenario, c.TestCase.Number })
                    .FirstOrDefaultAsync(ct);
                authorId = onCase?.AuthorId;
                testCaseId = onCase?.TestCaseId;
                where = onCase is null ? "" : $"on test case {TestCaseDisplay.CaseRef(onCase.Number)} {onCase.Scenario.ReplaceLineEndings(" ").Trim()}";
                break;
            default:
                var onProject = await db.ProjectComments.AsNoTracking()
                    .Where(c => c.Id == commentId)
                    .Select(c => new { c.AuthorId, c.ProjectId })
                    .FirstOrDefaultAsync(ct);
                authorId = onProject?.AuthorId;
                projectId = onProject?.ProjectId;
                // The bell already shows which project, so the message needn't repeat its name.
                where = "on the project dashboard";
                break;
        }

        if (authorId is null || authorId == reactorId)
        {
            return;
        }

        var reactor = await db.Users.AsNoTracking()
            .Where(u => u.Id == reactorId)
            .Select(u => new { u.FullName, u.UserName })
            .FirstOrDefaultAsync(ct);
        var who = reactor is null ? "Someone" : MentionName(reactor.FullName, reactor.UserName);
        var thumbs = reaction == Reaction.Up ? "a thumbs up" : "a thumbs down";
        var message = Truncate($"{who} gave your comment {thumbs} {where}", 500);

        if (await HasUnreadAsync(db, authorId, message, defectId, testCaseId, projectId, ct))
        {
            return;
        }

        await AddAsync(db, [authorId], NotificationTrigger.CommentReaction, defectId, testCaseId, projectId, message, reactorId, ct);
    }

    /// <summary>The text a mention of this user is written as ("@" + this) — the same
    /// display name the mention picker offers.</summary>
    public static string MentionName(string? fullName, string? userName) =>
        !string.IsNullOrWhiteSpace(fullName) ? fullName : userName ?? "Unknown";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Scenario(TestCase testCase) => testCase.Scenario.ReplaceLineEndings(" ").Trim();

    private static Task<bool> HasUnreadAsync(
        ApplicationDbContext db, string userId, string message, Guid? defectId, Guid? testCaseId, Guid? projectId,
        CancellationToken ct) =>
        db.Notifications.AnyAsync(n =>
            n.UserId == userId && n.ReadUtc == null && n.Message == message
            && n.DefectId == defectId && n.TestCaseId == testCaseId && n.ProjectId == projectId, ct);

    private Task<IReadOnlyList<string>> AddAsync(
        ApplicationDbContext db, IEnumerable<string> userIds, NotificationTrigger trigger, Guid defectId, string message,
        string? actingUserId, CancellationToken ct) =>
        AddAsync(db, userIds, trigger, defectId, null, null, message, actingUserId, ct);

    /// <summary>Notifies whichever of <paramref name="userIds"/> want <paramref name="trigger"/> —
    /// never the person who did it — and returns who was notified.</summary>
    private async Task<IReadOnlyList<string>> AddAsync(
        ApplicationDbContext db, IEnumerable<string> userIds, NotificationTrigger trigger,
        Guid? defectId, Guid? testCaseId, Guid? projectId, string message, string? actingUserId, CancellationToken ct)
    {
        // The person who triggered the event never needs telling about their own action.
        var candidates = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && id != actingUserId)
            .Distinct()
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var recipients = await WhoWantsAsync(db, candidates, trigger, ct);
        if (recipients.Count == 0)
        {
            return [];
        }

        var now = timeProvider.GetUtcNow();
        db.Notifications.AddRange(recipients.Select(userId => new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DefectId = defectId,
            TestCaseId = testCaseId,
            ProjectId = projectId,
            Message = message,
            CreatedUtc = now,
        }));
        await db.SaveChangesAsync(ct);
        return recipients;
    }

    /// <summary>Of <paramref name="userIds"/>, those whose notification choices (or, where they
    /// haven't chosen, the defaults for their role) include <paramref name="trigger"/>.</summary>
    private static async Task<List<string>> WhoWantsAsync(
        ApplicationDbContext db, List<string> userIds, NotificationTrigger trigger, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.NotificationsOn, u.NotificationsOff })
            .ToListAsync(ct);
        var developers = (await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where r.Name == Roles.Dev && userIds.Contains(ur.UserId)
            select ur.UserId).ToListAsync(ct)).ToHashSet();

        return users
            .Where(u => NotificationTriggers.Wants(trigger, u.NotificationsOn, u.NotificationsOff, developers.Contains(u.Id)))
            .Select(u => u.Id)
            .ToList();
    }
}
