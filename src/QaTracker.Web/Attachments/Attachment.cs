using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Attachments;

/// <summary>
/// A file uploaded for reference on a project, a test case, a defect, or one comment on a
/// test case / defect. Belongs to exactly one of <see cref="ProjectId"/>, <see cref="TestCaseId"/>,
/// <see cref="DefectId"/>, <see cref="TestCaseCommentId"/>, <see cref="DefectCommentId"/> — enforced
/// by <see cref="AttachmentService"/>, not the schema — so every context shares one table, service
/// and download proxy while each owner still gets a real FK with cascade delete. Comment files are
/// shown with their comment, not in the owner's Attachments list.
/// </summary>
public class Attachment
{
    public Guid Id { get; set; }

    /// <summary>Original filename, shown in the UI and used for the download's Content-Disposition.</summary>
    [Required]
    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Key of the object in the configured storage bucket/container.</summary>
    [Required]
    [MaxLength(500)]
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Optional caption, e.g. what the file shows.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Position among the owner's attachments, ascending.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset UploadedUtc { get; set; }

    public string UploadedById { get; set; } = string.Empty;

    public ApplicationUser? UploadedBy { get; set; }

    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    public Guid? TestCaseId { get; set; }

    public TestCase? TestCase { get; set; }

    public Guid? DefectId { get; set; }

    public Defect? Defect { get; set; }

    public Guid? TestCaseCommentId { get; set; }

    public TestCaseComment? TestCaseComment { get; set; }

    public Guid? DefectCommentId { get; set; }

    public DefectComment? DefectComment { get; set; }
}
