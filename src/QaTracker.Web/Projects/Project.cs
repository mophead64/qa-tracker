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

    // ---- Testing window + effort estimates (see QaEffort for the arithmetic) -----------

    /// <summary>First day of testing. Set together with <see cref="TestingEndDate"/>, or neither.</summary>
    public DateOnly? TestingStartDate { get; set; }

    /// <summary>Last day of testing — the same as the start for a one-day window.</summary>
    public DateOnly? TestingEndDate { get; set; }

    /// <summary>Testing starts at midday: the first day counts as half a day.</summary>
    public bool TestingStartsMidday { get; set; }

    /// <summary>Testing ends at midday: the last day counts as half a day.</summary>
    public bool TestingEndsMidday { get; set; }

    /// <summary>Estimated development effort in days (half-day steps).</summary>
    public decimal? EstimatedDevDays { get; set; }

    /// <summary>A QA estimate typed in directly, in days — wins over the percentage.</summary>
    public decimal? EstimatedQaDays { get; set; }

    /// <summary>This project's QA effort as a % of dev days; null uses the system default.</summary>
    public int? QaEffortPercent { get; set; }

    /// <summary>Template new defects in this project start with; null = use the system default.</summary>
    public Defects.DefectTemplate? DefaultDefectTemplate { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>Id of the user (expected to be a QA) who created the project.</summary>
    public string CreatedById { get; set; } = string.Empty;

    public ApplicationUser? CreatedBy { get; set; }

    public List<ProjectLink> Links { get; set; } = [];

    /// <summary>QA and Dev users assigned to this project's team.</summary>
    public List<ApplicationUser> Members { get; set; } = [];
}
