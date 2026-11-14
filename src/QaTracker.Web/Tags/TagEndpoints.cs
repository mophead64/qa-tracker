using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;

namespace QaTracker.Web.Tags;

/// <summary>
/// Form posts for tags: putting them on / taking them off a project from its dashboard (anyone
/// who can manage projects — typing a new name creates the tag for every project), and the
/// System settings tags page (creating there is QA only; editing / deleting a tag, from that page or
/// a dashboard, is for anyone who can manage projects).
/// </summary>
public static class TagEndpoints
{
    public const string AdminPath = "/admin/tags";

    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var projects = endpoints.MapGroup("/projects/{projectId:guid}/tags")
            .RequireAuthorization(Policies.ManageProjects);

        projects.MapPost("", async (
            Guid projectId, ClaimsPrincipal principal, TagService tags,
            [FromForm(Name = "tagName")] string? name, [FromForm] TagColour? colour, CancellationToken ct) =>
        {
            var result = await tags.AddToProjectAsync(projectId, name, ActorId(principal), colour ?? TagColour.Gray, ct);
            return result.Succeeded
                ? Results.LocalRedirect($"~/projects/{projectId}#tags")
                : Results.LocalRedirect(
                    $"~/projects/{projectId}?tagError={Uri.EscapeDataString(result.Error!)}"
                    + $"&tagName={Uri.EscapeDataString(Tag.Clean(name))}&tagColour={colour ?? TagColour.Gray}#tags");
        });

        projects.MapPost("/{tagId:guid}/remove", async (
            Guid projectId, Guid tagId, ClaimsPrincipal principal, TagService tags, CancellationToken ct) =>
        {
            await tags.RemoveFromProjectAsync(projectId, tagId, ActorId(principal), ct);
            return Results.LocalRedirect($"~/projects/{projectId}#tags");
        });

        var admin = endpoints.MapGroup(AdminPath).RequireAuthorization(p => p.RequireRole(Roles.QA));

        admin.MapPost("", async (
            TagService tags, [FromForm(Name = "tagName")] string? name, [FromForm] TagColour? colour, CancellationToken ct) =>
        {
            var result = await tags.CreateAsync(name, colour ?? TagColour.Gray, ct);
            return result.Succeeded
                ? Results.LocalRedirect($"~{AdminPath}?saved={result.TagId}#tag-{result.TagId}")
                : Results.LocalRedirect(
                    $"~{AdminPath}?error={Uri.EscapeDataString(result.Error!)}&name={Uri.EscapeDataString(Tag.Clean(name))}");
        });

        // Renaming / recolouring / deleting a tag, from the edit modal on the tags page or a project's
        // dashboard: anyone who can manage projects. The modal posts returnUrl (a same-site path) to
        // come back to; a rejected name comes back with ?tagEdit={id}&tagEditError=… so the modal
        // reopens with the error.
        var tag = endpoints.MapGroup("/tags/{tagId:guid}").RequireAuthorization(Policies.ManageProjects);

        tag.MapPost("", async (
            Guid tagId, ClaimsPrincipal principal, TagService tags,
            [FromForm(Name = "tagName")] string? name, [FromForm] TagColour? colour, [FromForm] string? returnUrl,
            CancellationToken ct) =>
        {
            var result = await tags.UpdateAsync(tagId, name, colour ?? TagColour.Gray, ActorId(principal), ct);
            return result.Succeeded
                ? Back(returnUrl)
                : Back(returnUrl, $"tagEdit={tagId}&tagEditError={Uri.EscapeDataString(result.Error!)}");
        });

        tag.MapPost("/delete", async (
            Guid tagId, ClaimsPrincipal principal, TagService tags, [FromForm] string? returnUrl, CancellationToken ct) =>
        {
            await tags.DeleteAsync(tagId, ActorId(principal), ct);
            return Back(returnUrl);
        });

        return endpoints;
    }

    private static string? ActorId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Back to a same-site path (else the tags page), optionally with an error query in
    /// place of the path's own query; any #fragment is kept.</summary>
    private static IResult Back(string? returnUrl, string? errorQuery = null)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            returnUrl = AdminPath;
        }

        if (errorQuery is null)
        {
            return Results.LocalRedirect($"~{returnUrl}");
        }

        var hashAt = returnUrl.IndexOf('#');
        var hash = hashAt >= 0 ? returnUrl[hashAt..] : "";
        var path = (hashAt >= 0 ? returnUrl[..hashAt] : returnUrl).Split('?')[0];
        return Results.LocalRedirect($"~{path}?{errorQuery}{hash}");
    }
}
