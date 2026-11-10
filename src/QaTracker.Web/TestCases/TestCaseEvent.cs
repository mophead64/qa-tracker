using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;

namespace QaTracker.Web.TestCases;

/// <summary>What happened to a test case, as recorded in its history.</summary>
public enum TestCaseEventKind
{
    Created = 0,

    /// <summary>Scenario and/or steps changed. <see cref="TestCaseEvent.ToValue"/> names what changed.</summary>
    Edited = 1,

    /// <summary>Moved to another scope. From/To hold the scope names at the time.</summary>
    Moved = 2,

    /// <summary>Result changed. From/To hold <see cref="TestResult"/> names; From is null when unknown (backfilled).</summary>
    ResultChanged = 3,

    /// <summary>A file was added to the case's Attachments. <see cref="TestCaseEvent.ToValue"/> is the filename.</summary>
    FileAdded = 4,

    /// <summary>A file was removed from the case's Attachments. <see cref="TestCaseEvent.FromValue"/> is the filename.</summary>
    FileRemoved = 5,
}

/// <summary>
/// One entry in a test case's execution history — the timeline on the detail page. Written
/// alongside the change it describes (by <see cref="TestCaseService"/> and
/// <see cref="Attachments.AttachmentService"/>) and never edited afterwards.
/// </summary>
public class TestCaseEvent
{
    public Guid Id { get; set; }

    public Guid TestCaseId { get; set; }

    public TestCase? TestCase { get; set; }

    public TestCaseEventKind Kind { get; set; }

    /// <summary>Who did it. Null once that user is deleted — history outlives accounts.</summary>
    public string? ActorId { get; set; }

    public ApplicationUser? Actor { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    /// <summary>Value before the change (old result, old scope name, removed filename).</summary>
    [MaxLength(500)]
    public string? FromValue { get; set; }

    /// <summary>Value after the change (new result, new scope name, added filename, edited fields).</summary>
    [MaxLength(500)]
    public string? ToValue { get; set; }
}
