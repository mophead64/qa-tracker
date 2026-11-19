using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Board;

public sealed record BoardCommentView(
    Guid Id, string AuthorId, string AuthorName, string Body, DateTimeOffset CreatedUtc,
    IReadOnlyList<CommentAttachmentView> Attachments, IReadOnlyList<CommentReactionView> Reactions);

public enum BoardLinkKind
{
    Scope,
    TestCase,
    Defect,
}

/// <summary>A link from a board item, as shown: what it points at, its ID ("S-1", "T-4", "D-2") and title.</summary>
/// <param name="Status">The target's state as a label (scope kind, test result, defect status); <paramref name="StatusClass"/> its badge.</param>
public sealed record BoardLinkView(Guid Id, BoardLinkKind Kind, Guid TargetId, string Ref, string Title, string? Status,
    string StatusClass = "badge badge-gray");

/// <summary>A board item as seen from a defect or test case it's linked to.</summary>
public sealed record LinkedBoardItem(Guid Id, BoardItemType Type, int Number, string Title, BoardColumn Column, bool Done)
{
    public string Ref => BoardDisplay.Ref(Type, Number);
}

/// <summary>A board's stories and bugs per column, and its tasks not yet complete (dashboard, projects list).</summary>
public sealed record BoardCounts(int New, int InProgress, int Testing, int Closed, int TasksToDo)
{
    public static readonly BoardCounts None = new(0, 0, 0, 0, 0);

    /// <summary>Stories and bugs not yet closed.</summary>
    public int Open => New + InProgress + Testing;

    public int Total => Open + Closed;
}

public sealed record BoardEventView(Guid Id, BoardEventKind Kind, string ActorName, DateTimeOffset OccurredUtc,
    string? FromValue, string? ToValue);

/// <summary>
/// Everything the board pages and endpoints do. Item changes go to the project's
/// <see cref="IBoardProvider"/> (<see cref="BoardProviders"/>); links to test cases / scopes /
/// defects, comments, history, live-page stamps and notifications are always QA Tracker's own.
/// </summary>
public sealed class BoardService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    BoardProviders providers,
    AttachmentService attachments,
    NotificationService notifications,
    TimeProvider time)
{
    // ---- the project's board ----------------------------------------------------------------

    /// <summary>The project's board mode, or null when the project doesn't exist.</summary>
    public async Task<BoardMode?> ModeAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Projects.Where(p => p.Id == projectId).Select(p => (BoardMode?)p.BoardMode).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> IsEnabledAsync(Guid projectId, CancellationToken ct = default) =>
        await ModeAsync(projectId, ct) is { } mode && mode != BoardMode.Off;

    /// <summary>
    /// Switches the board on or off (items are kept while it's off), recording "enabled / disabled
    /// the development board" in the project's history when it goes from off to on or back.
    /// True when that changed anything.
    /// </summary>
    public async Task<bool> SetModeAsync(Guid projectId, BoardMode mode, string? actorId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null || project.BoardMode == mode)
        {
            return false;
        }

        var wasOn = project.BoardMode != BoardMode.Off;
        project.BoardMode = mode;
        if (wasOn != (mode != BoardMode.Off))
        {
            Projects.ProjectHistory.Record(db, projectId,
                wasOn ? Projects.ProjectEventKind.BoardDisabled : Projects.ProjectEventKind.BoardEnabled,
                actorId, time.GetUtcNow());
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<IBoardProvider> ProviderAsync(Guid projectId, CancellationToken ct) =>
        providers.For(await ModeAsync(projectId, ct) ?? BoardMode.Off);

    /// <summary>
    /// Board counts for each of <paramref name="projectIds"/> whose board is on (others are left
    /// out), from QA Tracker's own copy of the items.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, BoardCounts>> CountByProjectAsync(IEnumerable<Guid> projectIds, CancellationToken ct = default)
    {
        var ids = projectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, BoardCounts>();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.BoardItems.AsNoTracking()
            .Where(i => ids.Contains(i.ProjectId) && i.Project!.BoardMode != BoardMode.Off)
            .GroupBy(i => new { i.ProjectId, i.Type, i.Column, i.Done })
            .Select(g => new { g.Key.ProjectId, g.Key.Type, g.Key.Column, g.Key.Done, Count = g.Count() })
            .ToListAsync(ct);
        var enabled = await db.Projects.Where(p => ids.Contains(p.Id) && p.BoardMode != BoardMode.Off).Select(p => p.Id).ToListAsync(ct);

        return enabled.ToDictionary(id => id, id =>
        {
            var mine = rows.Where(r => r.ProjectId == id).ToList();
            int In(BoardColumn column) => mine.Where(r => r.Type != BoardItemType.Task && r.Column == column).Sum(r => r.Count);
            return new BoardCounts(In(BoardColumn.New), In(BoardColumn.InProgress), In(BoardColumn.Testing), In(BoardColumn.Closed),
                mine.Where(r => r.Type == BoardItemType.Task && !r.Done).Sum(r => r.Count));
        });
    }

    public async Task<BoardCounts> CountAsync(Guid projectId, CancellationToken ct = default) =>
        (await CountByProjectAsync([projectId], ct)).GetValueOrDefault(projectId, BoardCounts.None);

    // ---- items --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BoardItem>> ListAsync(Guid projectId, CancellationToken ct = default) =>
        await (await ProviderAsync(projectId, ct)).ListAsync(projectId, ct);

    public async Task<BoardItem?> GetAsync(Guid projectId, Guid itemId, CancellationToken ct = default) =>
        await (await ProviderAsync(projectId, ct)).GetAsync(projectId, itemId, ct);

    /// <summary>A story or bug, or (with <paramref name="parentId"/>) a task on one. Links it to
    /// <paramref name="fromDefectId"/> when created from a defect, and tells the assignee — except for
    /// imported items (<see cref="BoardCreateOptions.Imported"/>), so a big import doesn't flood the bell.</summary>
    public async Task<BoardItem> CreateAsync(Guid projectId, BoardItemType type, BoardItemInput input, string actorId,
        Guid? parentId = null, Guid? fromDefectId = null, BoardCreateOptions? options = null, CancellationToken ct = default)
    {
        if (type == BoardItemType.Task && parentId is null)
        {
            throw new ArgumentException("A task needs a story or bug to belong to.", nameof(parentId));
        }

        var item = await (await ProviderAsync(projectId, ct)).CreateAsync(projectId, type, input, parentId, actorId, options, ct);
        if (fromDefectId is { } defectId)
        {
            await LinkAsync(projectId, item.Id, BoardLinkKind.Defect, defectId, actorId, ct);
        }
        if (item.AssignedToId is { } assignee && options is not { Imported: true })
        {
            await notifications.NotifyBoardAssignedAsync(item, assignee, actorId, ct);
        }
        return item;
    }

    public async Task UpdateAsync(Guid projectId, Guid itemId, BoardItemInput input, string actorId, CancellationToken ct = default)
    {
        var provider = await ProviderAsync(projectId, ct);
        var before = await provider.GetAsync(projectId, itemId, ct);
        if (before is null)
        {
            return;
        }

        await provider.UpdateAsync(itemId, input, actorId, ct);
        await NotifyIfReassignedAsync(before, input.AssignedToId, actorId, ct);
    }

    /// <summary>
    /// Moves a card (drag, or the item page's column menu). Changing column runs the workflow rules
    /// (<see cref="ApplyWorkflowAsync"/>); reordering within a column doesn't.
    /// </summary>
    public async Task MoveAsync(Guid projectId, Guid itemId, BoardColumn column, int index, string actorId, CancellationToken ct = default)
    {
        var provider = await ProviderAsync(projectId, ct);
        var before = await provider.GetAsync(projectId, itemId, ct);
        if (before is null)
        {
            return;
        }

        await provider.MoveAsync(itemId, column, index, actorId, ct);
        if (before.Type != BoardItemType.Task && before.Column != column)
        {
            await ApplyWorkflowAsync(provider, before, column, actorId, ct);
        }
    }

    /// <summary>
    /// Who a story or bug goes to when it changes column (the assignee is told, unless they moved it):
    /// <list type="bullet">
    /// <item>Into <b>In progress</b>: back to its developer when it comes back from Testing or Closed;
    /// otherwise to whoever moved it, who becomes its developer.</item>
    /// <item>Into <b>Testing</b>: back to the QA who tested it before, else to the project's QA with the
    /// fewest open board items (then by name). Whoever had it in progress counts as its developer.</item>
    /// <item>Into <b>Closed</b>: unassigned. Out of Testing, whoever had it is recorded as having tested
    /// it; with nobody on it (or from any other column), whoever closed it is recorded instead.</item>
    /// <item>Into <b>New</b>: the assignee stays. Leaving Closed clears tested / closed by.</item>
    /// </list>
    /// </summary>
    private async Task ApplyWorkflowAsync(IBoardProvider provider, BoardItem before, BoardColumn to, string actorId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.BoardItems.FirstAsync(i => i.Id == before.Id, ct);
        var from = before.Column;
        var next = before.AssignedToId;

        if (from == BoardColumn.Closed)
        {
            item.TestedById = null;
            item.ClosedById = null;
        }

        switch (to)
        {
            case BoardColumn.InProgress:
                if (from is BoardColumn.Testing or BoardColumn.Closed && item.DevelopedById is not null)
                {
                    next = item.DevelopedById;
                }
                else
                {
                    next = actorId;
                    item.DevelopedById = actorId;
                }
                break;

            case BoardColumn.Testing:
                if (from == BoardColumn.InProgress && before.AssignedToId is { } developer)
                {
                    item.DevelopedById ??= developer;
                }
                next = item.QaById is { } qa && await IsProjectQaAsync(db, before.ProjectId, qa, ct)
                    ? qa
                    : await PickQaAsync(db, before.ProjectId, ct);
                item.QaById = next ?? item.QaById;
                break;

            case BoardColumn.Closed:
                if (from == BoardColumn.Testing && before.AssignedToId is { } tester)
                {
                    item.TestedById = tester;
                }
                else
                {
                    item.ClosedById = actorId;
                }
                next = null;
                break;
        }

        await db.SaveChangesAsync(ct);
        if (next != before.AssignedToId)
        {
            await provider.SetAssigneeAsync(before.Id, next, actorId, ct);
            await NotifyIfReassignedAsync(before, next, actorId, ct);
        }
    }

    private static IQueryable<string> ProjectQaIds(ApplicationDbContext db, Guid projectId) =>
        from p in db.Projects.Where(p => p.Id == projectId)
        from m in p.Members
        join ur in db.UserRoles on m.Id equals ur.UserId
        join r in db.Roles on ur.RoleId equals r.Id
        where r.Name == Roles.QA
        select m.Id;

    private static Task<bool> IsProjectQaAsync(ApplicationDbContext db, Guid projectId, string userId, CancellationToken ct) =>
        ProjectQaIds(db, projectId).AnyAsync(id => id == userId, ct);

    /// <summary>The team's QA with the fewest open board items (stories / bugs not closed, tasks not done), then by name; null without one.</summary>
    private static async Task<string?> PickQaAsync(ApplicationDbContext db, Guid projectId, CancellationToken ct)
    {
        var qaIds = await ProjectQaIds(db, projectId).ToListAsync(ct);
        if (qaIds.Count == 0)
        {
            return null;
        }

        var load = await db.BoardItems
            .Where(i => i.ProjectId == projectId && i.AssignedToId != null && qaIds.Contains(i.AssignedToId)
                && (i.Type == BoardItemType.Task ? !i.Done : i.Column != BoardColumn.Closed))
            .GroupBy(i => i.AssignedToId!)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var names = await db.Users.Where(u => qaIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FullName ?? u.UserName ?? u.Id })
            .ToListAsync(ct);
        return names
            .OrderBy(u => load.GetValueOrDefault(u.Id))
            .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .Select(u => u.Id)
            .First();
    }

    public async Task SetAssigneeAsync(Guid projectId, Guid itemId, string? assigneeId, string actorId, CancellationToken ct = default)
    {
        var provider = await ProviderAsync(projectId, ct);
        var before = await provider.GetAsync(projectId, itemId, ct);
        if (before is null)
        {
            return;
        }

        await provider.SetAssigneeAsync(itemId, assigneeId, actorId, ct);
        await NotifyIfReassignedAsync(before, assigneeId, actorId, ct);
    }

    public async Task SetDoneAsync(Guid projectId, Guid taskId, bool done, string actorId, CancellationToken ct = default) =>
        await (await ProviderAsync(projectId, ct)).SetDoneAsync(taskId, done, actorId, ct);

    public async Task MoveTaskAsync(Guid projectId, Guid taskId, int index, CancellationToken ct = default) =>
        await (await ProviderAsync(projectId, ct)).MoveTaskAsync(taskId, index, ct);

    /// <summary>Deletes the item — with a story's / bug's tasks — and every file on them.</summary>
    public async Task DeleteAsync(Guid projectId, Guid itemId, CancellationToken ct = default)
    {
        await attachments.PurgeForBoardItemAsync(itemId, ct);
        await (await ProviderAsync(projectId, ct)).DeleteAsync(itemId, ct);
    }

    private async Task NotifyIfReassignedAsync(BoardItem before, string? assigneeId, string actorId, CancellationToken ct)
    {
        var next = string.IsNullOrWhiteSpace(assigneeId) ? null : assigneeId;
        if (next is not null && next != before.AssignedToId)
        {
            await notifications.NotifyBoardAssignedAsync(before, next, actorId, ct);
        }
    }

    // ---- links ---------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BoardLinkView>> ListLinksAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var links = await db.BoardItemLinks.AsNoTracking()
            .Where(l => l.BoardItemId == itemId)
            .Include(l => l.TestScope)
            .Include(l => l.TestCase)
            .Include(l => l.Defect)
            .ToListAsync(ct);

        return links.Select(l => l switch
            {
                { TestScope: { } s } => new BoardLinkView(l.Id, BoardLinkKind.Scope, s.Id, TestCaseDisplay.ScopeRef(s.Number), s.Name,
                    TestCaseDisplay.KindLabel(s.Kind)),
                { TestCase: { } t } => new BoardLinkView(l.Id, BoardLinkKind.TestCase, t.Id, TestCaseDisplay.CaseRef(t.Number), t.Scenario,
                    TestCaseDisplay.ResultLabel(t.Result), TestCaseDisplay.ResultBadgeClass(t.Result)),
                { Defect: { } d } => new BoardLinkView(l.Id, BoardLinkKind.Defect, d.Id, DefectDisplay.Ref(d.Number), d.Summary,
                    DefectDisplay.StatusLabel(d.Status), DefectDisplay.StatusBadgeClass(d.Status)),
                _ => null,
            })
            .OfType<BoardLinkView>()
            .OrderBy(l => l.Kind).ThenBy(l => l.Ref.Length).ThenBy(l => l.Ref, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Links the item to a scope, test case or defect in the same project (no-op if it already is,
    /// or the target isn't in the project).</summary>
    public async Task LinkAsync(Guid projectId, Guid itemId, BoardLinkKind kind, Guid targetId, string actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.BoardItems.AnyAsync(i => i.Id == itemId && i.ProjectId == projectId, ct))
        {
            return;
        }

        string? label = kind switch
        {
            BoardLinkKind.Scope => await db.TestScopes.Where(s => s.Id == targetId && s.ProjectId == projectId)
                .Select(s => "S-" + s.Number + " " + s.Name).FirstOrDefaultAsync(ct),
            BoardLinkKind.TestCase => await db.TestCases.Where(t => t.Id == targetId && t.ProjectId == projectId)
                .Select(t => "T-" + t.Number + " " + t.Scenario).FirstOrDefaultAsync(ct),
            _ => await db.Defects.Where(d => d.Id == targetId && d.ProjectId == projectId)
                .Select(d => "D-" + d.Number + " " + d.Summary).FirstOrDefaultAsync(ct),
        };
        if (label is null)
        {
            return;
        }

        var exists = kind switch
        {
            BoardLinkKind.Scope => await db.BoardItemLinks.AnyAsync(l => l.BoardItemId == itemId && l.TestScopeId == targetId, ct),
            BoardLinkKind.TestCase => await db.BoardItemLinks.AnyAsync(l => l.BoardItemId == itemId && l.TestCaseId == targetId, ct),
            _ => await db.BoardItemLinks.AnyAsync(l => l.BoardItemId == itemId && l.DefectId == targetId, ct),
        };
        if (exists)
        {
            return;
        }

        var now = time.GetUtcNow();
        db.BoardItemLinks.Add(new BoardItemLink
        {
            Id = Guid.NewGuid(),
            BoardItemId = itemId,
            TestScopeId = kind == BoardLinkKind.Scope ? targetId : null,
            TestCaseId = kind == BoardLinkKind.TestCase ? targetId : null,
            DefectId = kind == BoardLinkKind.Defect ? targetId : null,
            CreatedUtc = now,
        });
        BoardHistory.Record(db, itemId, BoardEventKind.Linked, actorId, now, toValue: label.ReplaceLineEndings(" "));
        await db.SaveChangesAsync(ct);
    }

    public async Task UnlinkAsync(Guid projectId, Guid itemId, Guid linkId, string actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var link = await db.BoardItemLinks
            .Include(l => l.TestScope).Include(l => l.TestCase).Include(l => l.Defect)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.BoardItemId == itemId && l.BoardItem!.ProjectId == projectId, ct);
        if (link is null)
        {
            return;
        }

        var label = link switch
        {
            { TestScope: { } s } => $"{TestCaseDisplay.ScopeRef(s.Number)} {s.Name}",
            { TestCase: { } t } => $"{TestCaseDisplay.CaseRef(t.Number)} {t.Scenario}",
            { Defect: { } d } => $"{DefectDisplay.Ref(d.Number)} {d.Summary}",
            _ => null,
        };
        db.BoardItemLinks.Remove(link);
        BoardHistory.Record(db, itemId, BoardEventKind.Unlinked, actorId, time.GetUtcNow(), fromValue: label?.ReplaceLineEndings(" "));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Board items linked to a defect (for the defect page).</summary>
    public Task<IReadOnlyList<LinkedBoardItem>> ListForDefectAsync(Guid defectId, CancellationToken ct = default) =>
        LinkedAsync(l => l.DefectId == defectId, ct);

    /// <summary>Board items linked to a test case, or to its scope (for the test case page).</summary>
    public Task<IReadOnlyList<LinkedBoardItem>> ListForTestCaseAsync(Guid testCaseId, Guid scopeId, CancellationToken ct = default) =>
        LinkedAsync(l => l.TestCaseId == testCaseId || l.TestScopeId == scopeId, ct);

    private async Task<IReadOnlyList<LinkedBoardItem>> LinkedAsync(
        System.Linq.Expressions.Expression<Func<BoardItemLink, bool>> which, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.BoardItemLinks.AsNoTracking()
            .Where(which)
            .Where(l => l.BoardItem!.Project!.BoardMode != BoardMode.Off)
            .Select(l => new LinkedBoardItem(l.BoardItem!.Id, l.BoardItem.Type, l.BoardItem.Number, l.BoardItem.Title,
                l.BoardItem.Column, l.BoardItem.Done))
            .ToListAsync(ct);
        return items.DistinctBy(i => i.Id).OrderBy(i => i.Type).ThenBy(i => i.Number).ToList();
    }

    // ---- comments -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<BoardCommentView>> ListCommentsAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var comments = await db.BoardItemComments.AsNoTracking()
            .Where(c => c.BoardItemId == itemId)
            .Include(c => c.Author)
            .Include(c => c.Attachments)
            .Include(c => c.Reactions).ThenInclude(r => r.User)
            .AsSplitQuery()
            .ToListAsync(ct);

        return comments
            .OrderBy(c => c.CreatedUtc)
            .Select(c => new BoardCommentView(c.Id, c.AuthorId, Name(c.Author), c.Body.Trim(), c.CreatedUtc,
                c.Attachments.OrderBy(a => a.SortOrder).Select(CommentAttachmentView.From).ToList(),
                CommentReactionView.ListFrom(c.Reactions)))
            .ToList();
    }

    public async Task<Guid> AddCommentAsync(Guid projectId, Guid itemId, string authorId, string body,
        IReadOnlyCollection<string>? mentionedUserIds = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.BoardItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.ProjectId == projectId, ct);
        if (item is null)
        {
            return Guid.Empty;
        }

        var comment = new BoardItemComment
        {
            Id = Guid.NewGuid(),
            BoardItemId = itemId,
            AuthorId = authorId,
            Body = body.Trim(),
            CreatedUtc = time.GetUtcNow(),
        };
        db.BoardItemComments.Add(comment);
        await db.SaveChangesAsync(ct);
        await notifications.NotifyBoardMentionedAsync(item, authorId, comment.Body, mentionedUserIds, ct);
        return comment.Id;
    }

    /// <summary>The comment's item id if it's on this project's board (for the delete endpoint's checks).</summary>
    public async Task<(Guid ItemId, string AuthorId)?> FindCommentAsync(Guid projectId, Guid commentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.BoardItemComments.AsNoTracking()
            .Where(c => c.Id == commentId && c.BoardItem!.ProjectId == projectId)
            .Select(c => new { c.BoardItemId, c.AuthorId })
            .FirstOrDefaultAsync(ct);
        return c is null ? null : (c.BoardItemId, c.AuthorId);
    }

    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default)
    {
        await attachments.PurgeForCommentAsync(AttachmentOwner.BoardItemComment, commentId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.BoardItemComments.Where(c => c.Id == commentId).ExecuteDeleteAsync(ct);
    }

    // ---- history ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BoardEventView>> ListHistoryAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var events = await db.BoardItemEvents.AsNoTracking()
            .Where(e => e.BoardItemId == itemId)
            .Include(e => e.Actor)
            .ToListAsync(ct);
        // In memory: SQLite (unit tests) can't ORDER BY DateTimeOffset.
        return events.OrderByDescending(e => e.OccurredUtc).ThenByDescending(e => e.Kind == BoardEventKind.Created ? 0 : 1)
            .Select(e => new BoardEventView(e.Id, e.Kind, e.Actor is null ? ApplicationUser.DeletedName : Name(e.Actor),
                e.OccurredUtc, e.FromValue, e.ToValue))
            .ToList();
    }

    // ---- live pages ---------------------------------------------------------------------------

    /// <summary>Fingerprint of the board page (<see cref="LiveVersion"/>): every card's column, place, title,
    /// assignee and task progress.</summary>
    public async Task<string> GetBoardLiveVersionAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = (await db.BoardItems.AsNoTracking()
                .Where(i => i.ProjectId == projectId)
                .Select(i => new { i.Id, i.Column, i.SortOrder, i.Title, i.AssignedToId, i.Done, i.ParentId })
                .ToListAsync(ct))
            .Select(i => $"{i.Id}:{i.Column}:{i.SortOrder}:{i.AssignedToId}:{i.Done}:{i.ParentId}:{i.Title}")
            .Order().ToList();
        return LiveVersion.Of(rows);
    }

    /// <summary>Fingerprint of an item's page, or null once it's gone.</summary>
    public async Task<string?> GetItemLiveVersionAsync(Guid projectId, Guid itemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.BoardItems.AsNoTracking()
            .Where(i => i.Id == itemId && i.ProjectId == projectId)
            .Select(i => new { i.Title, i.Body, i.Template, i.ExpectedResults, i.ActualResults, i.Column, i.Done, i.AssignedToId, i.SortOrder })
            .FirstOrDefaultAsync(ct);
        if (item is null)
        {
            return null;
        }

        var tasks = (await db.BoardItems.Where(t => t.ParentId == itemId)
                .Select(t => new { t.Id, t.Title, t.Done, t.SortOrder, t.AssignedToId }).ToListAsync(ct))
            .Select(t => $"{t.Id}:{t.Done}:{t.SortOrder}:{t.AssignedToId}:{t.Title}").Order().ToList();
        var events = await db.BoardItemEvents.CountAsync(e => e.BoardItemId == itemId, ct);
        var comments = (await db.BoardItemComments.Where(c => c.BoardItemId == itemId).Select(c => c.Id).ToListAsync(ct)).Order().ToList();
        var reactions = (await db.CommentReactions.Where(r => r.BoardItemComment!.BoardItemId == itemId)
                .Select(r => new { r.BoardItemCommentId, r.UserId, r.Reaction }).ToListAsync(ct))
            .Select(r => $"{r.BoardItemCommentId}:{r.UserId}:{r.Reaction}").Order().ToList();
        var links = await db.BoardItemLinks.CountAsync(l => l.BoardItemId == itemId, ct);
        var files = await db.Attachments.CountAsync(a => a.BoardItemId == itemId, ct);

        return LiveVersion.Of(item.Title, item.Body, item.Template, item.ExpectedResults, item.ActualResults,
            item.Column, item.Done, item.AssignedToId, item.SortOrder,
            tasks, events, comments, reactions, links, files);
    }

    private static string Name(ApplicationUser? user) =>
        user is null ? "Unknown"
        : user.DeletedUtc is not null ? ApplicationUser.DeletedName
        : !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.UserName ?? "Unknown";
}
