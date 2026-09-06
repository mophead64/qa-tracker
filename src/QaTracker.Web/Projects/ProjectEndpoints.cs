using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;

namespace QaTracker.Web.Projects;

/// <summary>
/// Form-post endpoints for project navigation. Switching the current project persists
/// the choice and refreshes the auth cookie so the new value lands in the claims — the
/// same reason the appearance setting posts rather than using an interactive handler.
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
