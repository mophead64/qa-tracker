using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;

namespace QaTracker.Web.Board;

/// <summary>What a board item is. Stories and bugs are cards on the board; tasks belong to one of them.</summary>
public enum BoardItemType
{
    Story,
    Bug,
    Task,
}

/// <summary>The board's columns, left to right. Stories and bugs move through them; tasks are just active or done.</summary>
public enum BoardColumn
{
    New,
    InProgress,
    Testing,
    Closed,
}

/// <summary>
/// Whether a project has a board, and where its items live. Only <see cref="Local"/> exists today;
/// an external provider (Azure DevOps…) would be another value here, with its own
/// <see cref="IBoardProvider"/> — see <see cref="BoardProviders"/>.
/// </summary>
public enum BoardMode
{
    Off,
    Local,
}

/// <summary>
/// A story, bug or task on a project's development board (a light take on Azure DevOps work items).
/// Numbered per type within the project (ST-3, BG-7, TK-12). A task's <see cref="ParentId"/> is the
/// story or bug it's part of; <see cref="SortOrder"/> orders cards within a column and tasks within
/// their parent. The External* fields are reserved for items mirrored from an external provider.
/// </summary>
public class BoardItem
{
    public const int MaxTitleLength = 200;

    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public BoardItemType Type { get; set; }

    public int Number { get; set; }

    [Required]
    [MaxLength(MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>The description — for an Advanced bug, its repro steps.</summary>
    public string? Body { get; set; }

    /// <summary>
    /// Bugs only: Simple (one description box) or Advanced (repro steps, expected and actual
    /// results), like a defect's template. Null for stories and tasks.
    /// </summary>
    public Defects.DefectTemplate? Template { get; set; }

    /// <summary>Advanced bugs: what should happen.</summary>
    public string? ExpectedResults { get; set; }

    /// <summary>Advanced bugs: what happens instead.</summary>
    public string? ActualResults { get; set; }

    /// <summary>Stories and bugs: which column. Tasks stay New.</summary>
    public BoardColumn Column { get; set; } = BoardColumn.New;

    /// <summary>Tasks: ticked off.</summary>
    public bool Done { get; set; }

    public Guid? ParentId { get; set; }

    public BoardItem? Parent { get; set; }

    public List<BoardItem> Tasks { get; set; } = [];

    public int SortOrder { get; set; }

    public string? AssignedToId { get; set; }

    public ApplicationUser? AssignedTo { get; set; }

    // ---- the workflow's memory (stories and bugs), kept by BoardService.MoveAsync ------------

    /// <summary>Who took it into In progress — it goes back to them when testing sends it back.</summary>
    public string? DevelopedById { get; set; }

    public ApplicationUser? DevelopedBy { get; set; }

    /// <summary>The QA it went to for testing — it goes back to them when it returns to Testing.</summary>
    public string? QaById { get; set; }

    public ApplicationUser? QaBy { get; set; }

    /// <summary>Closed out of Testing: the QA who had it.</summary>
    public string? TestedById { get; set; }

    public ApplicationUser? TestedBy { get; set; }

    /// <summary>Closed with nobody testing it: whoever closed it.</summary>
    public string? ClosedById { get; set; }

    public ApplicationUser? ClosedBy { get; set; }

    public string CreatedById { get; set; } = string.Empty;

    public ApplicationUser? CreatedBy { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>Reserved: the external provider an item was mirrored from (e.g. "AzureDevOps").</summary>
    [MaxLength(32)]
    public string? ExternalProvider { get; set; }

    [MaxLength(128)]
    public string? ExternalId { get; set; }

    [MaxLength(2048)]
    public string? ExternalUrl { get; set; }

    public List<BoardItemLink> Links { get; set; } = [];
}

public class BoardItemComment
{
    public Guid Id { get; set; }

    public Guid BoardItemId { get; set; }

    public BoardItem? BoardItem { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public ApplicationUser? Author { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public List<Attachment> Attachments { get; set; } = [];

    public List<CommentReaction> Reactions { get; set; } = [];
}

/// <summary>
/// A link from a story / bug / task to a test case scope, a test case or a defect — exactly one of
/// the three ids is set. Real foreign keys, so deleting the other side takes the link with it.
/// </summary>
public class BoardItemLink
{
    public Guid Id { get; set; }

    public Guid BoardItemId { get; set; }

    public BoardItem? BoardItem { get; set; }

    public Guid? TestScopeId { get; set; }

    public TestCases.TestScope? TestScope { get; set; }

    public Guid? TestCaseId { get; set; }

    public TestCases.TestCase? TestCase { get; set; }

    public Guid? DefectId { get; set; }

    public Defects.Defect? Defect { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}

public enum BoardEventKind
{
    Created,
    Edited,
    Moved,
    AssigneeChanged,
    TaskAdded,
    TaskCompleted,
    TaskReopened,
    Linked,
    Unlinked,
    FileAdded,
    FileRemoved,
}

/// <summary>A line in a board item's history. Values are display snapshots (names, refs) so they survive deletes.</summary>
public class BoardItemEvent
{
    public Guid Id { get; set; }

    public Guid BoardItemId { get; set; }

    public BoardItem? BoardItem { get; set; }

    public BoardEventKind Kind { get; set; }

    public string? ActorId { get; set; }

    public ApplicationUser? Actor { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    [MaxLength(500)]
    public string? FromValue { get; set; }

    [MaxLength(500)]
    public string? ToValue { get; set; }
}

public static class BoardHistory
{
    public static void Record(ApplicationDbContext db, Guid itemId, BoardEventKind kind, string? actorId,
        DateTimeOffset when, string? fromValue = null, string? toValue = null) =>
        db.BoardItemEvents.Add(new BoardItemEvent
        {
            Id = Guid.NewGuid(),
            BoardItemId = itemId,
            Kind = kind,
            ActorId = actorId,
            OccurredUtc = when,
            FromValue = Clip(fromValue),
            ToValue = Clip(toValue),
        });

    private static string? Clip(string? value) => value is { Length: > 500 } ? value[..499] + "…" : value;
}
