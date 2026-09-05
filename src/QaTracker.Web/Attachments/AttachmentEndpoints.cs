using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using QaTracker.Web.Storage;

namespace QaTracker.Web.Attachments;

/// <summary>
/// Attachment download proxy (the app never links directly to the storage account), plus
/// the static-rendered upload and delete form posts used by <c>AttachmentPanel</c>.
/// Only an explicit allow-list of image/video types renders inline on download; everything
/// else (including SVG, which can carry a script) always downloads as an attachment.
/// </summary>
public static class AttachmentEndpoints
{
    private static readonly HashSet<string> InlineContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/avif",
        "video/mp4", "video/webm", "video/ogg",
    };

    /// <summary>Text fields of the upload form (the file itself binds separately).</summary>
    public sealed record UploadForm(AttachmentOwner Owner, Guid OwnerId, string? Description, string? ReturnUrl);

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // ?download=true forces a Save-As even for types that otherwise render inline.
        endpoints.MapGet("/attachments/{id:guid}", DownloadAsync).RequireAuthorization();

        var group = endpoints.MapGroup("/attachments").RequireAuthorization();
        group.MapPost("/upload", UploadAsync);
        group.MapPost("/{id:guid}/delete", DeleteAsync);

        return endpoints;
    }

    private static async Task DownloadAsync(
        HttpContext http, Guid id, string? download, AttachmentService attachments, CancellationToken ct)
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
    }

    private static async Task<IResult> UploadAsync(
        ClaimsPrincipal principal,
        AttachmentService attachments,
        IFileStorage storage,
        IConfiguration configuration,
        [FromForm] UploadForm form,
        IFormFile? file,
        CancellationToken ct)
    {
        var back = Local(form.ReturnUrl);
        if (!storage.IsConfigured || file is null || file.Length == 0)
        {
            return RedirectWithError(back, "Choose a file to upload.");
        }

        var maxBytes = StorageOptions.ResolveMaxUploadBytes(configuration);
        if (file.Length > maxBytes)
        {
            return RedirectWithError(back, $"That file is larger than the {maxBytes / (1024 * 1024)} MB limit.");
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        await using var stream = file.OpenReadStream();
        await attachments.UploadAsync(
            form.Owner, form.OwnerId, stream, file.FileName, contentType, file.Length, form.Description, userId, ct);

        return Results.LocalRedirect($"~{back}");
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal principal, AttachmentService attachments, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (await attachments.CanModifyAsync(id, userId, principal.IsInRole(Data.Roles.QA), ct))
        {
            await attachments.DeleteAsync(id, ct);
        }

        return Results.LocalRedirect($"~{Local(returnUrl)}");
    }

    private static string Local(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";

    private static IResult RedirectWithError(string path, string error)
    {
        var separator = path.Contains('?') ? '&' : '?';
        return Results.LocalRedirect($"~{path}{separator}attachmentError={Uri.EscapeDataString(error)}");
    }
}
