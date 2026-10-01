using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;

namespace QaTracker.Web.Projects;

/// <summary>
/// Form-post endpoints for project navigation and the dashboard's controls. Switching the
/// current project persists the choice and refreshes the auth cookie so the new value lands
/// in the claims — the same reason the appearance setting posts rather than using an
/// interactive handler.
/// </summary>
public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/projects").RequireAuthorization();

        // Switch the current project from the top-bar dropdown / project picker.
        group.MapPost("/switch", (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ProjectService projects,
            [FromForm] Guid? projectId) =>
            SetCurrentProjectAsync(principal, userManager, signInManager, projects, projectId));

        // Remember the All Projects list filter for this user.
        group.MapPost("/filter", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            [FromForm] ProjectListFilter filter) =>
        {
            var user = await userManager.GetUserAsync(principal);
            if (user is not null && Enum.IsDefined(filter) && user.ProjectListFilter != filter)
            {
                user.ProjectListFilter = filter;
                await userManager.UpdateAsync(user);
            }
            return Results.LocalRedirect("~/projects");
        });

        // Change a project's lifecycle status from the dashboard dropdown.
        group.MapPost("/{projectId:guid}/status", async (
            Guid projectId, ProjectService projects, [FromForm] ProjectStatus status) =>
        {
            await projects.SetStatusAsync(projectId, status);
            return Results.LocalRedirect($"~/projects/{projectId}");
        }).RequireAuthorization(Policies.ManageProjects);

        // Add / remove team members from the dashboard's Team card. The add form posts one
        // "userIds" field per checked user — minimal-API [FromForm] can't bind a string[],
        // so read the collection directly (an IFormCollection parameter still enforces
        // antiforgery).
        group.MapPost("/{projectId:guid}/team/add", async (
            Guid projectId, ProjectService projects, IFormCollection form) =>
        {
            var userIds = form["userIds"].Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToArray();
            await projects.AddMembersAsync(projectId, userIds);
            return Results.LocalRedirect($"~/projects/{projectId}");
        }).RequireAuthorization(Policies.ManageProjects);

        group.MapPost("/{projectId:guid}/team/remove", async (
            Guid projectId, ProjectService projects, [FromForm] string userId) =>
        {
            await projects.RemoveMemberAsync(projectId, userId);
            return Results.LocalRedirect($"~/projects/{projectId}");
        }).RequireAuthorization(Policies.ManageProjects);

        // Comments on the dashboard: any authenticated user, like defect / test-case comments.
        // Multipart: the comment text plus optional files (stored as attachments on the comment).
        group.MapPost("/{projectId:guid}/comments", async (
            Guid projectId, ClaimsPrincipal principal, ProjectService projects,
            AttachmentService attachments, SystemSettingsService settings, ILoggerFactory loggerFactory,
            [FromForm] string body, HttpRequest request, IFormFileCollection files, CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(body)
                || !await projects.ExistsAsync(projectId, ct))
            {
                return BackToComments(projectId);
            }

            var uploads = AttachmentEndpoints.CommentFiles(files);
            var error = await AttachmentEndpoints.ValidateCommentFilesAsync(uploads, attachments, settings, ct);
            if (error is null)
            {
                var commentId = await projects.AddCommentAsync(projectId, userId, body, request.Form["mentions"].OfType<string>().ToList(), ct);
                if (uploads.Count > 0)
                {
                    error = await AttachmentEndpoints.AttachToCommentAsync(
                        AttachmentOwner.ProjectComment, commentId, uploads, userId, attachments,
                        loggerFactory.CreateLogger("QaTracker.Web.Projects.Comments"), ct);
                    if (error is not null)
                    {
                        await projects.DeleteCommentAsync(commentId, ct);
                    }
                }
            }

            return error is null
                ? BackToComments(projectId)
                : Results.LocalRedirect($"~/projects/{projectId}?commentError={Uri.EscapeDataString(error)}#comments");
        });

        group.MapPost("/{projectId:guid}/comments/{commentId:guid}/delete", async (
            Guid projectId, Guid commentId, ClaimsPrincipal principal, ProjectService projects) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var comment = (await projects.ListCommentsAsync(projectId)).FirstOrDefault(c => c.Id == commentId);
            if (comment is not null && (principal.IsInRole(Roles.QA) || comment.AuthorId == userId))
            {
                await projects.DeleteCommentAsync(commentId);
            }
            return BackToComments(projectId);
        });

        // Landing target after creating a project: make the new one current, then show it.
        group.MapGet("/switch", (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ProjectService projects,
            [FromQuery] Guid? created) =>
            SetCurrentProjectAsync(principal, userManager, signInManager, projects, created));

        return group;
    }

    // The comments card sits at the foot of a long dashboard; land back on it, not the top.
    private static IResult BackToComments(Guid projectId) =>
        Results.LocalRedirect($"~/projects/{projectId}#comments");

    private static async Task<IResult> SetCurrentProjectAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ProjectService projects,
        Guid? projectId)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.LocalRedirect("~/");
        }

        if (projectId is { } id && await projects.ExistsAsync(id))
        {
            user.CurrentProjectId = id;
            await userManager.UpdateAsync(user);
            await signInManager.RefreshSignInAsync(user);
            return Results.LocalRedirect($"~/projects/{id}");
        }

        user.CurrentProjectId = null;
        await userManager.UpdateAsync(user);
        await signInManager.RefreshSignInAsync(user);
        return Results.LocalRedirect("~/");
    }
}
