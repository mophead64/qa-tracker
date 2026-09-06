using System.ComponentModel.DataAnnotations;

namespace QaTracker.Web.Defects;

/// <summary>
/// One piece of evidence attached to a defect: a description plus an optional link to a
/// problematic resource. Uploaded files (see <c>AttachmentService</c>) are a further kind.
/// </summary>
public class DefectEvidence
{
    public Guid Id { get; set; }

    public Guid DefectId { get; set; }

    public Defect? Defect { get; set; }

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional link to the example / problematic resource.</summary>
    public string? Url { get; set; }

    public int SortOrder { get; set; }
}
