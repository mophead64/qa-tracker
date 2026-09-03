using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Data;

namespace QaTracker.Web.TestCases;

/// <summary>A note left on a test case by a dev or QA (e.g. why it failed, repro detail).</summary>
public class TestCaseComment
{
    public Guid Id { get; set; }

    public Guid TestCaseId { get; set; }

    public TestCase? TestCase { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public ApplicationUser? Author { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
}
