using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Notifications;

/// <summary>
/// A notification for one user about something that happened on a defect (new defect,
/// reassignment, a status change, a comment), or a mention in a test-case or project
/// comment. It links to exactly one of <see cref="DefectId"/>, <see cref="TestCaseId"/> or
/// <see cref="ProjectId"/>.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    /// <summary>The defect this is about. Null for a notification about a test case.</summary>
    public Guid? DefectId { get; set; }

    public Defect? Defect { get; set; }

    /// <summary>The test case this is about (a mention in a test-case comment). Null for a
    /// notification about a defect.</summary>
    public Guid? TestCaseId { get; set; }

    public TestCase? TestCase { get; set; }

    /// <summary>The project this is about (a mention in a comment on the project dashboard).
    /// Null for a notification about a defect or test case.</summary>
    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Null while unread; set once the user opens the notification bell. Notifications
    /// persist after being read — the only way they go away is a "Clear all" (hard delete).</summary>
    public DateTimeOffset? ReadUtc { get; set; }
}
