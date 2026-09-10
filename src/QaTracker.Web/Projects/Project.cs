using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;

namespace QaTracker.Web.Projects;

/// <summary>
/// A short engagement that dev and QA collaborate on. Owns its notes, a set of custom
/// links, its test cases and its defects.
/// </summary>
public class Project
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>One-line summary shown under the project name on the all-projects list.</summary>
    [MaxLength(280)]
    public string? Brief { get; set; }

    /// <summary>Free-form notes shown on the project dashboard.</summary>
    public string? Notes { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.NotStarted;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>Id of the user (expected to be a QA) who created the project.</summary>
    public string CreatedById { get; set; } = string.Empty;

    public ApplicationUser? CreatedBy { get; set; }

    public List<ProjectLink> Links { get; set; } = [];

    /// <summary>QA and Dev users assigned to this project's team.</summary>
    public List<ApplicationUser> Members { get; set; } = [];
}
