using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Auth;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;

namespace QaTracker.Web.Board;

/// <summary>
/// Form posts and small JSON calls behind the board pages. Changing items (moving cards, the
/// assignee, tasks, links) needs <see cref="Policies.ManageProjects"/> — QAs, and developers when
/// System settings let them manage projects; comments are open to everyone signed in, Viewers
/// included. Every item route 404s unless the item is on this project's board and the board is on.
/// </summary>
public static class BoardEndpoints
{
    public static IEndpointRouteBuilder MapBoardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var board = endpoints.MapGroup("/projects/{projectId:guid}/board").RequireAuthorization();

        // The importer's example file: a story with tasks, an Advanced bug with a task and a Simple bug.
        board.MapGet("/import/template.csv", () =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(BoardImportService.Template()), "text/csv", "board-import-template.csv"));

        // The board page's live stamp (live-page.js polls it).
        board.MapGet("/live", async (Guid projectId, HttpContext http, BoardService boards, CancellationToken ct) =>
            await boards.IsEnabledAsync(projectId, ct)
                ? LiveVersion.Json(http, await boards.GetBoardLiveVersionAsync(projectId, ct))
                : Results.NotFound());

        var item = board.MapGroup("/{itemId:guid}").AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var boards = http.RequestServices.GetRequiredService<BoardService>();
            if (!Guid.TryParse(http.Request.RouteValues["projectId"]?.ToString(), out var projectId)
                || !Guid.TryParse(http.Request.RouteValues["itemId"]?.ToString(), out var itemId)
                || !await boards.IsEnabledAsync(projectId, http.RequestAborted)
                || await boards.GetAsync(projectId, itemId, http.RequestAborted) is null)
            {
                return Results.NotFound();
            }
            return await next(context);
        });

        item.MapGet("/live", async (Guid projectId, Guid itemId, HttpContext http, BoardService boards, CancellationToken ct) =>
            await boards.GetItemLiveVersionAsync(projectId, itemId, ct) is { } version ? LiveVersion.Json(http, version) : Results.NotFound());

        item.MapGet("/history", (Guid projectId, Guid itemId) =>
            new Microsoft.AspNetCore.Http.HttpResults.RazorComponentResult<Components.Shared.BoardHistoryPanel>(
                new { ProjectId = projectId, ItemId = itemId }));

        // ---- changing items (QA, and developers allowed to manage projects) ---------------------

        // A card dropped on the board (board.js, fetch → JSON) or a column picked on the item page (form → back).
        item.MapPost("/move", async (Guid projectId, Guid itemId, HttpContext http, ClaimsPrincipal principal, BoardService boards,
            [FromForm] BoardColumn column, [FromForm] int? index, [FromForm] string? returnUrl, CancellationToken ct) =>
        {
            await boards.MoveAsync(projectId, itemId, column, index ?? int.MaxValue, UserId(principal), ct);
            return http.Request.Headers.XRequestedWith == "fetch" ? Results.Ok(new { ok = true }) : Back(projectId, itemId, returnUrl);
        }).RequireAuthorization(Policies.ManageProjects);

        item.MapPost("/assignee", async (Guid projectId, Guid itemId, ClaimsPrincipal principal, BoardService boards,
            [FromForm] string? assigneeId, [FromForm] string? returnUrl, CancellationToken ct) =>
        {
            await boards.SetAssigneeAsync(projectId, itemId, assigneeId, UserId(principal), ct);
            return Back(projectId, itemId, returnUrl);
        }).RequireAuthorization(Policies.ManageProjects);

        // A new task on a story or bug (the item page's "Add a task" row).
        item.MapPost("/tasks", async (Guid projectId, Guid itemId, ClaimsPrincipal principal, BoardService boards,
            [FromForm] string? title, [FromForm] string? assigneeId, CancellationToken ct) =>
        {
            var parent = await boards.GetAsync(projectId, itemId, ct);
            if (parent is { Type: not BoardItemType.Task } && !string.IsNullOrWhiteSpace(title))
            {
                var clipped = title.Trim() is { Length: > BoardItem.MaxTitleLength } tooLong ? tooLong[..BoardItem.MaxTitleLength] : title.Trim();
                await boards.CreateAsync(projectId, BoardItemType.Task, new BoardItemInput(clipped, null, assigneeId), UserId(principal),
                    parentId: itemId, ct: ct);
            }
            return Results.LocalRedirect($"~/{AppUrls.BoardItem(projectId, itemId)}#tasks");
        }).RequireAuthorization(Policies.ManageProjects);

        // Ticking a task off (or back on) — from its parent's checklist or the task's own page.
        item.MapPost("/done", async (Guid projectId, Guid itemId, ClaimsPrincipal principal, BoardService boards,
            [FromForm] bool done, [FromForm] string? returnUrl, CancellationToken ct) =>
        {
            await boards.SetDoneAsync(projectId, itemId, done, UserId(principal), ct);
            return Back(projectId, itemId, returnUrl);
        }).RequireAuthorization(Policies.ManageProjects);

        // A task dragged to a new place in its parent's list (board.js → JSON).
        item.MapPost("/task-order", async (Guid projectId, Guid itemId, BoardService boards, [FromForm] int index, CancellationToken ct) =>
        {
            await boards.MoveTaskAsync(projectId, itemId, index, ct);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization(Policies.ManageProjects);

        // The "Link" modal: every ticked scope, test case and defect (values "Scope:{id}" etc.).
        item.MapPost("/links", async (Guid projectId, Guid itemId, ClaimsPrincipal principal, BoardService boards,
            HttpRequest request, CancellationToken ct) =>
        {
            foreach (var value in request.Form["link"].OfType<string>())
            {
                var parts = value.Split(':', 2);
                if (parts.Length == 2 && Enum.TryParse<BoardLinkKind>(parts[0], out var kind) && Guid.TryParse(parts[1], out var targetId))
                {
                    await boards.LinkAsync(projectId, itemId, kind, targetId, UserId(principal), ct);
                }
            }
            return Results.LocalRedirect($"~/{AppUrls.BoardItem(projectId, itemId)}#links");
        }).RequireAuthorization(Policies.ManageProjects);

        item.MapPost("/links/{linkId:guid}/delete", async (Guid projectId, Guid itemId, Guid linkId, ClaimsPrincipal principal,
            BoardService boards, CancellationToken ct) =>
        {
            await boards.UnlinkAsync(projectId, itemId, linkId, UserId(principal), ct);
            return Results.LocalRedirect($"~/{AppUrls.BoardItem(projectId, itemId)}#links");
        }).RequireAuthorization(Policies.ManageProjects);

        // ---- comments (everyone signed in) ----------------------------------------------------------

        item.MapPost("/comments", async (
            Guid projectId, Guid itemId, ClaimsPrincipal principal, BoardService boards,
            AttachmentService attachments, SystemSettingsService settings, ILoggerFactory loggerFactory,
            [FromForm] string body, HttpRequest request, IFormFileCollection files, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var page = AppUrls.BoardItem(projectId, itemId);
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(body))
            {
                return Results.LocalRedirect($"~/{page}#{CommentPaging.Anchor}");
            }

            var uploads = AttachmentEndpoints.CommentFiles(files);
            var error = await AttachmentEndpoints.ValidateCommentFilesAsync(uploads, attachments, settings, ct);
            var commentId = Guid.Empty;
            if (error is null)
            {
                commentId = await boards.AddCommentAsync(projectId, itemId, userId, body, request.Form["mentions"].OfType<string>().ToList(), ct);
                if (uploads.Count > 0)
                {
                    error = await AttachmentEndpoints.AttachToCommentAsync(AttachmentOwner.BoardItemComment, commentId, uploads, userId,
                        attachments, loggerFactory.CreateLogger("QaTracker.Web.Board.Comments"), ct);
                    if (error is not null)
                    {
                        await boards.DeleteCommentAsync(commentId, ct);
                    }
                }
            }

            return error is null
                ? Results.LocalRedirect($"~/{page}#comment-{commentId}")
                : Results.LocalRedirect($"~/{page}?commentError={Uri.EscapeDataString(error)}#{CommentPaging.Anchor}");
        });

        item.MapPost("/comments/{commentId:guid}/delete", async (
            Guid projectId, Guid itemId, Guid commentId, ClaimsPrincipal principal, BoardService boards,
            [FromForm] string? commentsPage, CancellationToken ct) =>
        {
            if (await boards.FindCommentAsync(projectId, commentId, ct) is { } comment && comment.ItemId == itemId
                && (principal.IsInRole(Roles.QA) || comment.AuthorId == UserId(principal)))
            {
                await boards.DeleteCommentAsync(commentId, ct);
            }
            return Results.LocalRedirect($"~/{AppUrls.BoardItem(projectId, itemId)}{CommentPaging.QueryFrom(commentsPage)}#{CommentPaging.Anchor}");
        });

        return endpoints;
    }

    private static string UserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    /// <summary>Back to the posted same-site returnUrl, else the item's own page.</summary>
    private static IResult Back(Guid projectId, Guid itemId, string? returnUrl) =>
        Results.LocalRedirect($"~{ReturnUrls.LocalOr(returnUrl, "/" + AppUrls.BoardItem(projectId, itemId))}");
}
