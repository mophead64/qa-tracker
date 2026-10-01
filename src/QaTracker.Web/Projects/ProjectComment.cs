using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;

namespace QaTracker.Web.Projects;

/// <summary>A note left on a project's dashboard — project-wide discussion that isn't about
/// one particular test case or defect.</summary>
public class ProjectComment
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public ApplicationUser? Author { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Optional files attached to this comment.</summary>
    public List<Attachment> Attachments { get; set; } = [];

    /// <summary>Thumbs up / down from readers.</summary>
    public List<CommentReaction> Reactions { get; set; } = [];
}
