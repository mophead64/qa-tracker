using Microsoft.Net.Http.Headers;

namespace QaTracker.Web.Attachments;

/// <summary>
/// Proxies attachment downloads — the app never links directly to the storage account.
/// Only an explicit allow-list of image/video types render inline; everything else
/// (including SVG, which can carry a script) always downloads as an attachment.
/// </summary>
public static class AttachmentEndpoints
{
    private static readonly HashSet<string> InlineContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/avif",
        "video/mp4", "video/webm", "video/ogg",
    };

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // ?download=true forces a Save-As even for types that otherwise render inline.
        // Bound as a string (not bool?) so an unparsable value 400s the request instead of
        // silently falling back — anything other than "true" is treated as "no".
        endpoints.MapGet("/attachments/{id:guid}", async (
            HttpContext http,
            Guid id,
            string? download,
            AttachmentService attachments,
            CancellationToken ct) =>
        {
            var content = await attachments.OpenAsync(id, ct);
            if (content is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await using var stream = content.Content;

            var forceDownload = string.Equals(download, "true", StringComparison.OrdinalIgnoreCase);
            var inline = !forceDownload && InlineContentTypes.Contains(content.ContentType);
            var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            disposition.SetHttpFileName(content.FileName);

            http.Response.ContentType = content.ContentType;
            http.Response.Headers.ContentDisposition = disposition.ToString();
            http.Response.Headers.XContentTypeOptions = "nosniff";

            await stream.CopyToAsync(http.Response.Body, ct);
        }).RequireAuthorization();

        return endpoints;
    }
}
