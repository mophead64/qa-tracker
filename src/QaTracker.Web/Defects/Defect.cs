using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Defects;

/// <summary>
/// A defect raised within a project. Usually raised from a failing <see cref="TestCase"/>
/// but may be standalone; the link is optional and can be added or removed later.
/// </summary>
public class Defect
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    /// <summary>Per-project sequential number, shown as "D-{Number}". Assigned on create.</summary>
    public int Number { get; set; }

    /// <summary>
    /// Test cases this defect is linked to (many-to-many). A defect may be raised against
    /// several cases, or none. Links are removed if either side is deleted; the defect itself survives.
    /// </summary>
    public List<TestCase> TestCases { get; set; } = [];

    /// <summary>Short statement of the problem — the defect's identity, shown as the list label / detail heading.</summary>
    [Required]
    public string Summary { get; set; } = string.Empty;

    /// <summary>How to reproduce the problem, one step per line.</summary>
    public string? ReproSteps { get; set; }

    public string? ExpectedResults { get; set; }

    public string? ActualResults { get; set; }

    public DefectSeverity Severity { get; set; } = DefectSeverity.Medium;

    public DefectStatus Status { get; set; } = DefectStatus.NotFixed;

    public string? AssignedToId { get; set; }

    public ApplicationUser? AssignedTo { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string CreatedById { get; set; } = string.Empty;

    public ApplicationUser? CreatedBy { get; set; }
}
