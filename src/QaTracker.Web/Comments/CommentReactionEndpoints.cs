using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace QaTracker.Web.Comments;

/// <summary>
/// The thumbs up / down buttons under every comment post here. One endpoint serves all three
/// kinds of comment: anyone signed in can comment anywhere, so anyone can react anywhere too.
/// </summary>
public static class CommentReactionEndpoints
{
    public static IEndpointRouteBuilder MapCommentReactionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/comments/{kind}/{commentId:guid}/react", async (
            CommentKind kind, Guid commentId, ClaimsPrincipal principal, CommentReactionService reactions,
            [FromForm] Reaction reaction, [FromForm] string? returnUrl, CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId) && Enum.IsDefined(kind) && Enum.IsDefined(reaction))
            {
                await reactions.ReactAsync(kind, commentId, userId, reaction, ct);
            }

            return Back(returnUrl, commentId);
        }).RequireAuthorization();

        return endpoints;
    }

    // Back to the page the comment is on, scrolled to the comment itself (each comment's <li>
    // carries id="comment-{id}"). Same-site paths only; anything odd goes home.
    private static IResult Back(string? returnUrl, Guid commentId)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return Results.LocalRedirect("~/");
        }

        var path = returnUrl.Split('#')[0];
        return Results.LocalRedirect($"~{path}#comment-{commentId}");
    }
}
