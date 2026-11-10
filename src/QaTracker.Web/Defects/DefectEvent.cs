using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;

namespace QaTracker.Web.Defects;

/// <summary>What happened to a defect, as recorded in its history.</summary>
public enum DefectEventKind
{
    Created = 0,

    /// <summary>Summary / repro steps / expected / actual changed. <see cref="DefectEvent.ToValue"/> names what.</summary>
    Edited = 1,

    /// <summary>From/To hold <see cref="DefectStatus"/> names.</summary>
    StatusChanged = 2,

    /// <summary>From/To hold <see cref="DefectSeverity"/> names.</summary>
    SeverityChanged = 3,

    /// <summary>From/To hold the assignees' display names at the time; null = unassigned.</summary>
    AssigneeChanged = 4,

    /// <summary><see cref="DefectEvent.ToValue"/> is the linked test case's scenario.</summary>
    TestCaseLinked = 5,

    /// <summary><see cref="DefectEvent.FromValue"/> is the unlinked test case's scenario.</summary>
    TestCaseUnlinked = 6,

    /// <summary><see cref="DefectEvent.ToValue"/> is the filename.</summary>
    FileAdded = 7,

    /// <summary><see cref="DefectEvent.FromValue"/> is the filename.</summary>
    FileRemoved = 8,
}

/// <summary>
/// One entry in a defect's history — the timeline in its History modal. Written alongside the
/// change it describes (by <see cref="DefectService"/> and <see cref="Attachments.AttachmentService"/>)
/// and never edited. Mirrors <see cref="TestCases.TestCaseEvent"/>.
/// </summary>
public class DefectEvent
{
    public Guid Id { get; set; }

    public Guid DefectId { get; set; }

    public Defect? Defect { get; set; }

    public DefectEventKind Kind { get; set; }

    /// <summary>Who did it. Null once that user is deleted — history outlives accounts.</summary>
    public string? ActorId { get; set; }

    public ApplicationUser? Actor { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    [MaxLength(500)]
    public string? FromValue { get; set; }

    [MaxLength(500)]
    public string? ToValue { get; set; }
}
