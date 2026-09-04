using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;

namespace QaTracker.Web.Defects;

/// <summary>A note left on a defect by a dev or QA — the running conversation about the fix.</summary>
public class DefectComment
{
    public Guid Id { get; set; }

    public Guid DefectId { get; set; }

    public Defect? Defect { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public ApplicationUser? Author { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
}
