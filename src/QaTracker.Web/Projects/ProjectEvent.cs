using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;

namespace QaTracker.Web.Projects;

/// <summary>What happened to a project itself (as opposed to one of its test cases or defects).</summary>
public enum ProjectEventKind
{
    Created = 0,

    /// <summary>Details changed. <see cref="ProjectEvent.ToValue"/> names what ("name and notes").</summary>
    Edited = 1,

    /// <summary>From/To hold <see cref="ProjectStatus"/> names.</summary>
    StatusChanged = 2,

    /// <summary><see cref="ProjectEvent.ToValue"/> is the member's display name at the time.</summary>
    MemberAdded = 3,

    /// <summary><see cref="ProjectEvent.FromValue"/> is the member's display name at the time.</summary>
    MemberRemoved = 4,

    /// <summary><see cref="ProjectEvent.ToValue"/> is the scope name; <see cref="ProjectEvent.SubjectId"/> the scope.</summary>
    ScopeCreated = 5,

    /// <summary>From/To hold the old and new scope names; <see cref="ProjectEvent.SubjectId"/> the scope.</summary>
    ScopeRenamed = 6,

    /// <summary><see cref="ProjectEvent.FromValue"/> is the scope name (its test cases went with it).</summary>
    ScopeDeleted = 7,

    /// <summary><see cref="ProjectEvent.FromValue"/> is the scenario. The case's own history is gone with it.</summary>
    TestCaseDeleted = 8,

    /// <summary><see cref="ProjectEvent.FromValue"/> is "D-n Summary". The defect's own history is gone with it.</summary>
    DefectDeleted = 9,

    /// <summary>A CSV import. <see cref="ProjectEvent.ToValue"/> summarises it ("12 new, 3 updated").</summary>
    TestCasesImported = 10,

    /// <summary>A file added to the project's Attachments. <see cref="ProjectEvent.ToValue"/> is the filename.</summary>
    FileAdded = 11,

    /// <summary><see cref="ProjectEvent.FromValue"/> is the filename.</summary>
    FileRemoved = 12,

    /// <summary>A tag put on the project. <see cref="ProjectEvent.ToValue"/> is its name; <see cref="ProjectEvent.SubjectId"/> the tag.</summary>
    TagAdded = 13,

    /// <summary>A tag taken off the project. <see cref="ProjectEvent.FromValue"/> is its name.</summary>
    TagRemoved = 14,

    /// <summary>A tag on the project renamed (System settings). From/To hold the old and new names.</summary>
    TagRenamed = 15,

    /// <summary>A tag on the project deleted outright (System settings). <see cref="ProjectEvent.FromValue"/> is its name.</summary>
    TagDeleted = 16,
}

/// <summary>
/// One entry in a project's own history — shown, merged with its test cases' and defects'
/// history, in the project's activity feed. Also records deletions of test cases and defects,
/// whose own history is deleted with them. Written alongside the change it describes and never
/// edited. Mirrors <see cref="TestCases.TestCaseEvent"/>.
/// </summary>
public class ProjectEvent : ITimelineEvent
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public ProjectEventKind Kind { get; set; }

    /// <summary>Who did it. Null once that user is deleted — history outlives accounts.</summary>
    public string? ActorId { get; set; }

    public ApplicationUser? Actor { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    /// <summary>The scope (or tag) the entry is about, for scope and tag events (not a foreign
    /// key — it may since have been deleted; the feed only links a scope while it exists).</summary>
    public Guid? SubjectId { get; set; }

    [MaxLength(500)]
    public string? FromValue { get; set; }

    [MaxLength(500)]
    public string? ToValue { get; set; }
}
