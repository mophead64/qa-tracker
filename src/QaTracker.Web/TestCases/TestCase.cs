using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;

namespace QaTracker.Web.TestCases;

/// <summary>
/// A single test case within a <see cref="TestScope"/>. QAs author these; a failing
/// result is where defects get raised.
/// </summary>
public class TestCase
{
    public Guid Id { get; set; }

    public Guid TestScopeId { get; set; }

    public TestScope? TestScope { get; set; }

    /// <summary>The project the case's scope belongs to — kept on the case so its
    /// <see cref="Number"/> can be unique across the whole project. Moving a case between scopes
    /// keeps it (moves never cross projects).</summary>
    public Guid ProjectId { get; set; }

    /// <summary>The case's ID within its project, shown as "T-1" (<see cref="TestCaseDisplay.CaseRef"/>).
    /// Unique across every scope, allocated on creation and kept when the case moves scope — the
    /// CSV importer matches on it, so a case can be renamed or moved from a spreadsheet.</summary>
    public int Number { get; set; }

    /// <summary>The scenario this case covers, e.g. "When a user enters a correct email and password".</summary>
    [Required]
    public string Scenario { get; set; } = string.Empty;

    /// <summary>Steps to execute, one per line.</summary>
    public string? Steps { get; set; }

    public TestResult Result { get; set; } = TestResult.NotRun;

    /// <summary>Who recorded the current <see cref="Result"/> (passed, failed, blocked or
    /// inconclusive). Cleared when the case goes back to Not run.</summary>
    public string? TestedById { get; set; }

    public ApplicationUser? TestedBy { get; set; }

    /// <summary>When <see cref="TestedById"/> recorded the result.</summary>
    public DateTimeOffset? TestedUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string CreatedById { get; set; } = string.Empty;

    public ApplicationUser? CreatedBy { get; set; }

    /// <summary>Defects linked to this case (many-to-many; see <see cref="Defect.TestCases"/>).</summary>
    public List<Defect> Defects { get; set; } = [];
}
