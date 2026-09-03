using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;

namespace QaTracker.Web.TestCases;

/// <summary>
/// A single test case within a <see cref="TestScope"/>. QAs author these; a failing
/// result is where defects (Phase 4) get raised.
/// </summary>
public class TestCase
{
    public Guid Id { get; set; }

    public Guid TestScopeId { get; set; }

    public TestScope? TestScope { get; set; }

    /// <summary>The scenario this case covers, e.g. "When a user enters a correct email and password".</summary>
    [Required]
    public string Scenario { get; set; } = string.Empty;

    /// <summary>Steps to execute, one per line.</summary>
    public string? Steps { get; set; }

    public TestResult Result { get; set; } = TestResult.NotRun;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string CreatedById { get; set; } = string.Empty;

    public ApplicationUser? CreatedBy { get; set; }
}
