using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.Board;

/// <summary>
/// A story / bug / task's editable fields. <paramref name="Template"/>, <paramref name="ExpectedResults"/>
/// and <paramref name="ActualResults"/> only apply to bugs (ignored for stories and tasks); a bug
/// without a template is Simple.
/// </summary>
public sealed record BoardItemInput(string Title, string? Body, string? AssignedToId,
    Defects.DefectTemplate? Template = null, string? ExpectedResults = null, string? ActualResults = null);

/// <summary>
/// Where a new item starts (the CSV importer): a story / bug's column, or a task already complete.
/// <paramref name="Imported"/> marks its "created" history entry as coming from an import.
/// </summary>
public sealed record BoardCreateOptions(BoardColumn Column = BoardColumn.New, bool Done = false, bool Imported = false)
{
    public static readonly BoardCreateOptions Default = new();
}

/// <summary>
/// Where a project's board items live and how they change. The board pages and endpoints only ever
/// go through <see cref="BoardService"/>, which picks the provider for the project's
/// <see cref="BoardMode"/> (<see cref="BoardProviders"/>). Today that's always
/// <see cref="LocalBoardProvider"/> (QA Tracker's own database). An external provider (Azure DevOps…)
/// would implement this against its API, mirroring items into <see cref="BoardItem"/> rows (with the
/// External* fields set) so links, comments and history — always local — keep working.
/// </summary>
public interface IBoardProvider
{
    /// <summary>The project's stories and bugs (not tasks), with their tasks and assignees.</summary>
    Task<IReadOnlyList<BoardItem>> ListAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>One item (any type) in the project, with its parent, tasks and people; null if it isn't there.</summary>
    Task<BoardItem?> GetAsync(Guid projectId, Guid itemId, CancellationToken ct = default);

    /// <summary>A new story / bug (no parent) or task (parent = a story or bug), numbered per type.</summary>
    Task<BoardItem> CreateAsync(Guid projectId, BoardItemType type, BoardItemInput input, Guid? parentId, string actorId,
        BoardCreateOptions? options = null, CancellationToken ct = default);

    Task UpdateAsync(Guid itemId, BoardItemInput input, string actorId, CancellationToken ct = default);

    /// <summary>Puts a story or bug in <paramref name="column"/> at <paramref name="index"/> (0 = top).</summary>
    Task MoveAsync(Guid itemId, BoardColumn column, int index, string actorId, CancellationToken ct = default);

    Task SetAssigneeAsync(Guid itemId, string? assigneeId, string actorId, CancellationToken ct = default);

    Task SetDoneAsync(Guid taskId, bool done, string actorId, CancellationToken ct = default);

    /// <summary>Puts a task at <paramref name="index"/> among its parent's tasks.</summary>
    Task MoveTaskAsync(Guid taskId, int index, CancellationToken ct = default);

    /// <summary>Deletes the item (and a story's / bug's tasks).</summary>
    Task DeleteAsync(Guid itemId, CancellationToken ct = default);
}

/// <summary>Picks the provider for a project's board.</summary>
public sealed class BoardProviders(LocalBoardProvider local)
{
    public IBoardProvider For(BoardMode mode) => mode switch
    {
        // Future: BoardMode.AzureDevOps => azureDevOps, …
        _ => local,
    };
}

/// <summary>Board items in QA Tracker's own database.</summary>
public sealed class LocalBoardProvider(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider time) : IBoardProvider
{
    public async Task<IReadOnlyList<BoardItem>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.BoardItems.AsNoTracking()
            .Where(i => i.ProjectId == projectId && i.ParentId == null)
            .Include(i => i.AssignedTo)
            .Include(i => i.Tasks)
            .Include(i => i.Links)
            .AsSplitQuery()
            .ToListAsync(ct);
        return items.OrderBy(i => i.Column).ThenBy(i => i.SortOrder).ThenBy(i => i.Number).ToList();
    }

    public async Task<BoardItem?> GetAsync(Guid projectId, Guid itemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.BoardItems.AsNoTracking()
            .Where(i => i.Id == itemId && i.ProjectId == projectId)
            .Include(i => i.AssignedTo)
            .Include(i => i.CreatedBy)
            .Include(i => i.DevelopedBy)
            .Include(i => i.TestedBy)
            .Include(i => i.ClosedBy)
            .Include(i => i.Parent)
            .Include(i => i.Tasks).ThenInclude(t => t.AssignedTo)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);
        if (item is not null)
        {
            item.Tasks = item.Tasks.OrderBy(t => t.SortOrder).ThenBy(t => t.Number).ToList();
        }
        return item;
    }

    public async Task<BoardItem> CreateAsync(Guid projectId, BoardItemType type, BoardItemInput input, Guid? parentId,
        string actorId, BoardCreateOptions? options = null, CancellationToken ct = default)
    {
        options ??= BoardCreateOptions.Default;
        var column = type == BoardItemType.Task ? BoardColumn.New : options.Column;
        var now = time.GetUtcNow();
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var number = (await db.BoardItems.Where(i => i.ProjectId == projectId && i.Type == type)
                .MaxAsync(i => (int?)i.Number, ct) ?? 0) + 1;

            // New cards go to the bottom of New; new tasks to the end of their parent's list.
            var order = parentId is { } p
                ? await db.BoardItems.CountAsync(i => i.ParentId == p, ct)
                : await db.BoardItems.CountAsync(i => i.ProjectId == projectId && i.ParentId == null && i.Column == column, ct);

            var item = new BoardItem
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Type = type,
                Number = number,
                Title = input.Title.Trim(),
                Body = Normalize(input.Body),
                Template = type == BoardItemType.Bug ? input.Template ?? Defects.DefectTemplate.Simple : null,
                ExpectedResults = type == BoardItemType.Bug ? Normalize(input.ExpectedResults) : null,
                ActualResults = type == BoardItemType.Bug ? Normalize(input.ActualResults) : null,
                ParentId = type == BoardItemType.Task ? parentId : null,
                SortOrder = order,
                Column = column,
                Done = type == BoardItemType.Task && options.Done,
                AssignedToId = string.IsNullOrWhiteSpace(input.AssignedToId) ? null : input.AssignedToId,
                CreatedById = actorId,
                CreatedUtc = now,
                UpdatedUtc = now,
            };
            db.BoardItems.Add(item);
            BoardHistory.Record(db, item.Id, BoardEventKind.Created, actorId, now, toValue: options.Imported ? CreatedByImport : null);
            if (item.AssignedToId is { } assignee)
            {
                BoardHistory.Record(db, item.Id, BoardEventKind.AssigneeChanged, actorId, now, toValue: await NameOfAsync(db, assignee, ct));
            }
            if (item.ParentId is { } parent)
            {
                BoardHistory.Record(db, parent, BoardEventKind.TaskAdded, actorId, now, toValue: BoardDisplay.Label(item));
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return item;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // Someone else took the number at the same moment: try the next one.
            }
        }
    }

    public async Task UpdateAsync(Guid itemId, BoardItemInput input, string actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.BoardItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        var changed = new List<string>();
        var title = input.Title.Trim();
        if (item.Title != title)
        {
            item.Title = title;
            changed.Add("title");
        }
        var body = Normalize(input.Body);
        if (item.Body != body)
        {
            item.Body = body;
            changed.Add(item.Template == Defects.DefectTemplate.Advanced ? "repro steps" : "description");
        }
        if (item.Type == BoardItemType.Bug)
        {
            var template = input.Template ?? item.Template ?? Defects.DefectTemplate.Simple;
            if (item.Template != template)
            {
                item.Template = template;
                changed.Add("template");
            }
            var expected = Normalize(input.ExpectedResults);
            if (item.ExpectedResults != expected)
            {
                item.ExpectedResults = expected;
                changed.Add("expected results");
            }
            var actual = Normalize(input.ActualResults);
            if (item.ActualResults != actual)
            {
                item.ActualResults = actual;
                changed.Add("actual results");
            }
        }
        if (changed.Count > 0)
        {
            BoardHistory.Record(db, itemId, BoardEventKind.Edited, actorId, now, toValue: ChangedList(changed));
        }

        await SetAssigneeAsync(db, item, input.AssignedToId, actorId, now, ct);
        item.UpdatedUtc = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveAsync(Guid itemId, BoardColumn column, int index, string actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.BoardItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ParentId == null, ct);
        if (item is null || !Enum.IsDefined(column))
        {
            return;
        }

        var now = time.GetUtcNow();
        if (item.Column != column)
        {
            BoardHistory.Record(db, itemId, BoardEventKind.Moved, actorId, now,
                fromValue: BoardDisplay.ColumnLabel(item.Column), toValue: BoardDisplay.ColumnLabel(column));
            item.Column = column;
            item.UpdatedUtc = now;
        }

        // Renumber the target column with the item at its new place.
        var others = (await db.BoardItems
                .Where(i => i.ProjectId == item.ProjectId && i.ParentId == null && i.Column == column && i.Id != itemId)
                .ToListAsync(ct))
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Number).ToList();
        others.Insert(Math.Clamp(index, 0, others.Count), item);
        for (var i = 0; i < others.Count; i++)
        {
            others[i].SortOrder = i;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SetAssigneeAsync(Guid itemId, string? assigneeId, string actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.BoardItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (await SetAssigneeAsync(db, item, assigneeId, actorId, now, ct))
        {
            item.UpdatedUtc = now;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetDoneAsync(Guid taskId, bool done, string actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var task = await db.BoardItems.FirstOrDefaultAsync(i => i.Id == taskId && i.Type == BoardItemType.Task, ct);
        if (task is null || task.Done == done)
        {
            return;
        }

        var now = time.GetUtcNow();
        task.Done = done;
        task.UpdatedUtc = now;
        var kind = done ? BoardEventKind.TaskCompleted : BoardEventKind.TaskReopened;
        BoardHistory.Record(db, task.Id, kind, actorId, now);
        if (task.ParentId is { } parent)
        {
            BoardHistory.Record(db, parent, kind, actorId, now, toValue: BoardDisplay.Label(task));
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveTaskAsync(Guid taskId, int index, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var task = await db.BoardItems.FirstOrDefaultAsync(i => i.Id == taskId && i.ParentId != null, ct);
        if (task is null)
        {
            return;
        }

        var siblings = (await db.BoardItems.Where(i => i.ParentId == task.ParentId && i.Id != taskId).ToListAsync(ct))
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Number).ToList();
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), task);
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].SortOrder = i;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // A story's / bug's tasks go with it (cascade on ParentId).
        await db.BoardItems.Where(i => i.Id == itemId).ExecuteDeleteAsync(ct);
    }

    private static async Task<bool> SetAssigneeAsync(ApplicationDbContext db, BoardItem item, string? assigneeId, string actorId,
        DateTimeOffset now, CancellationToken ct)
    {
        var next = string.IsNullOrWhiteSpace(assigneeId) ? null : assigneeId;
        if (item.AssignedToId == next)
        {
            return false;
        }

        BoardHistory.Record(db, item.Id, BoardEventKind.AssigneeChanged, actorId, now,
            fromValue: item.AssignedToId is { } from ? await NameOfAsync(db, from, ct) : null,
            toValue: next is { } to ? await NameOfAsync(db, to, ct) : null);
        item.AssignedToId = next;
        return true;
    }

    private static async Task<string> NameOfAsync(ApplicationDbContext db, string userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId)
            .Select(u => u.FullName != null && u.FullName != "" ? u.FullName : u.UserName)
            .FirstOrDefaultAsync(ct) ?? "Unknown";

    /// <summary>The "created" history entry's value for an item from the CSV importer.</summary>
    public const string CreatedByImport = "import";

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>"title", "title and description", "title, template and expected results".</summary>
    private static string ChangedList(IReadOnlyList<string> changed) => changed.Count <= 2
        ? string.Join(" and ", changed)
        : $"{string.Join(", ", changed.Take(changed.Count - 1))} and {changed[^1]}";
}
