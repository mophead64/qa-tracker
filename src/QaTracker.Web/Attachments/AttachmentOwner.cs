namespace QaTracker.Web.Attachments;

/// <summary>
/// Which aggregate an <see cref="Attachment"/> belongs to. Service-facing only — not
/// persisted, since the populated owner FK column on the row already says which.
/// </summary>
public enum AttachmentOwner
{
    Project,
    TestCase,
    Defect,
}
