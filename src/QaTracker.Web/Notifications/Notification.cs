using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;

namespace QaTracker.Web.Notifications;

/// <summary>
/// A notification for one user about something that happened on a defect (new defect,
/// reassignment, a status change, a comment). There's no other kind of notification yet,
/// so it always links to the defect it's about.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public Guid DefectId { get; set; }

    public Defect? Defect { get; set; }

    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Null while unread; set once the user opens the notification bell. Notifications
    /// persist after being read — the only way they go away is a "Clear all" (hard delete).</summary>
    public DateTimeOffset? ReadUtc { get; set; }
}
