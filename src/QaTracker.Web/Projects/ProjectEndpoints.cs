using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
