using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Components.Shared;

namespace QaTracker.Web.Notifications;

/// <summary>
/// Endpoints behind the notification bell. The bell renders only a count in the page; its
/// dropdown fetches <c>GET /notifications/panel</c> when opened, so notification text is
/// never in the DOM of every page. <c>GET /notifications/feed</c> is polled by
/// <c>notification-poll.js</c> for the live badge + toasts. Dismiss is a plain form post
/// that redirects back.
/// </summary>
public static class NotificationEndpoints
{
    /// <summary>Path the client polls every ~45s — kept out of request logging and telemetry.</summary>
    public const string FeedPath = "/notifications/feed";

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/notifications").RequireAuthorization();

        group.MapGet("/panel", (ClaimsPrincipal principal, [FromQuery] string? returnUrl) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            return new RazorComponentResult<NotificationPanel>(new
            {
                UserId = userId,
                ReturnUrl = LocalReturnUrl(returnUrl),
            });
        });

        // Small JSON feed the bell polls for a live count + toast payloads. Every app
        // instance reads the same Notifications table, so it doesn't matter which one
        // answers — no backplane, sticky sessions or extra service needed.
        group.MapGet("/feed", async (ClaimsPrincipal principal, NotificationService notifications) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var items = await notifications.ListActiveAsync(userId);
            return Results.Json(new
            {
                count = items.Count,
                items = items.Select(n => new
                {
                    id = n.Id,
                    message = n.Message,
                    projectName = n.ProjectName,
                    url = $"/projects/{n.ProjectId}/defects/{n.DefectId}",
                    createdUtc = n.CreatedUtc,
                }),
            });
        });

        group.MapPost("/{id:guid}/dismiss", async (
            Guid id,
            ClaimsPrincipal principal,
            NotificationService notifications,
            [FromForm] string? returnUrl) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                await notifications.DismissAsync(id, userId);
            }

            return Results.LocalRedirect(LocalReturnUrl(returnUrl));
        });

        group.MapPost("/dismiss-all", async (
            ClaimsPrincipal principal,
            NotificationService notifications,
            [FromForm] string? returnUrl) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                await notifications.DismissAllAsync(userId);
            }

            return Results.LocalRedirect(LocalReturnUrl(returnUrl));
        });

        return endpoints;
    }

    // Only allow a same-site path back (leading single slash), otherwise fall back to home.
    private static string LocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
