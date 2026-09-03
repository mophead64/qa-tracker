using System.ComponentModel.DataAnnotations;

namespace QaTracker.Web.Projects;

/// <summary>
/// A user-defined link on a project (e.g. repo, staging environment, spec doc),
/// rendered as a labelled button on the project dashboard.
/// </summary>
public class ProjectLink
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    [Required]
    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    public string Url { get; set; } = string.Empty;

    /// <summary>Position of the link within the project, ascending.</summary>
    public int SortOrder { get; set; }
}
