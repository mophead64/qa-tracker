using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Comments;

/// <summary>Which kind of comment a reaction is on. Used in the reaction endpoint's route.</summary>
public enum CommentKind
{
    Project,
    TestCase,
    Defect,
}

/// <summary>A thumbs up or a thumbs down — a user has at most one of the two on a comment.</summary>
public enum Reaction
{
    Up,
    Down,
}

/// <summary>
/// One user's thumbs up / down on one comment. Belongs to exactly one of
/// <see cref="ProjectCommentId"/>, <see cref="TestCaseCommentId"/>, <see cref="DefectCommentId"/> —
/// the same one-table, one-of-several-FKs shape as <see cref="Attachments.Attachment"/> —
/// and is unique per user per comment.
/// </summary>
public class CommentReaction
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public Reaction Reaction { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public Guid? ProjectCommentId { get; set; }

    public ProjectComment? ProjectComment { get; set; }

    public Guid? TestCaseCommentId { get; set; }

    public TestCaseComment? TestCaseComment { get; set; }

    public Guid? DefectCommentId { get; set; }

    public DefectComment? DefectComment { get; set; }
}

/// <summary>One reaction as shown on a comment: who, and which way.</summary>
public sealed record CommentReactionView(string UserId, string UserName, Reaction Reaction)
{
    /// <summary>A comment's reactions in the order they were made, for its view record.</summary>
    public static IReadOnlyList<CommentReactionView> ListFrom(IEnumerable<CommentReaction> reactions) =>
        reactions
            .OrderBy(r => r.CreatedUtc)
            .Select(r => new CommentReactionView(r.UserId, DisplayName(r.User), r.Reaction))
            .ToList();

    private static string DisplayName(ApplicationUser? user) =>
        user is null ? "Unknown"
        : !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.UserName ?? "Unknown";
}
