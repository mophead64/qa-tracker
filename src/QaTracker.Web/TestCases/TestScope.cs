using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;

namespace QaTracker.Web.TestCases;

/// <summary>
/// A named area of testing within a project (e.g. "Authentication", "Checkout"). Groups
/// the individual <see cref="TestCase"/> rows under it and carries the functional /
/// non-functional distinction.
/// </summary>
public class TestScope
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public TestCaseKind Kind { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string CreatedById { get; set; } = string.Empty;

    public ApplicationUser? CreatedBy { get; set; }

    public List<TestCase> Cases { get; set; } = [];
}
