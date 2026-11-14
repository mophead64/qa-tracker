using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using QaTracker.Web.Admin;

namespace QaTracker.Web.Attachments;

/// <summary>
/// Attachment download proxy (the app never links directly to the storage account), plus
/// the static-rendered upload and delete form posts used by <c>AttachmentPanel</c>.
/// <c>/attachments/{id}</c> always serves a download; <c>/attachments/{id}/preview</c> serves an
/// allow-listed image / video / audio / PDF inline for the preview modal (see <see cref="AttachmentPreview"/>).
/// </summary>
public static class AttachmentEndpoints
{
    /// <summary>Text fields of the upload form (the file itself binds separately).</summary>
    public sealed record UploadForm(AttachmentOwner Owner, Guid OwnerId, string? Description, string? ReturnUrl);

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/attachments/{id:guid}", DownloadAsync).RequireAuthorization();
        endpoints.MapGet("/attachments/{id:guid}/preview", PreviewAsync).RequireAuthorization();

        var group = endpoints.MapGroup("/attachments").RequireAuthorization();
        group.MapPost("/upload", UploadAsync);
        group.MapPost("/{id:guid}/delete", DeleteAsync);

        return endpoints;
    }

    /// <summary>
    /// Serves a previewable attachment inline. Only allow-listed extensions, always with the
    /// canonical content type for the extension (never the uploader-supplied one) and nosniff.
    /// The file is buffered (uploads are size-capped) so byte ranges work — video seeking needs
    /// them, and Safari won't play video at all without.
    /// </summary>
    private static async Task<IResult> PreviewAsync(
        HttpContext http, Guid id, AttachmentService attachments, CancellationToken ct)
    {
        var content = await attachments.OpenAsync(id, ct);
        if (content is null)
        {
            return Results.NotFound();
        }

        await using var source = content.Content;
        if (AttachmentPreview.Resolve(content.FileName) is not { } preview)
        {
            return Results.NotFound();
        }

        var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(content.FileName);
        http.Response.Headers.ContentDisposition = disposition.ToString();
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "private, max-age=300";
        if (preview.Kind != PreviewKind.Pdf)
        {
            // Media is only ever a subresource; if one is opened directly, nothing else may load.
            // (Not on PDFs: a restrictive policy on the document breaks the browser's PDF viewer.)
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; media-src 'self'; style-src 'unsafe-inline'";
        }

        return Results.Stream(buffer, preview.ContentType, enableRangeProcessing: true);
    }

    private static async Task DownloadAsync(
        HttpContext http, Guid id, AttachmentService attachments, CancellationToken ct)
    {
        var content = await attachments.OpenAsync(id, ct);
        if (content is null)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using var stream = content.Content;

        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(content.FileName);

        http.Response.ContentType = content.ContentType;
        http.Response.Headers.ContentDisposition = disposition.ToString();
        http.Response.Headers.XContentTypeOptions = "nosniff";

        await stream.CopyToAsync(http.Response.Body, ct);
    }

    private static async Task<IResult> UploadAsync(
        HttpContext http,
        ClaimsPrincipal principal,
        AttachmentService attachments,
        SystemSettingsService settings,
        Notifications.NotificationService notifications,
        [FromForm] UploadForm form,
        IFormFile? file,
        CancellationToken ct)
    {
        // The "Add attachments" modal (attachment-upload.js) posts one file per request with this
        // header and wants JSON back; the no-JS path still gets a redirect-with-error.
        var wantsJson = http.Request.Headers.XRequestedWith == "fetch";
        var back = Local(form.ReturnUrl);

        if (!attachments.StorageConfigured || file is null || file.Length == 0)
        {
            return Fail(wantsJson, back, "Choose a file to upload.");
        }

        var maxBytes = await settings.MaxUploadBytesAsync(ct);
        if (file.Length > maxBytes)
        {
            return Fail(wantsJson, back, $"{file.FileName} is larger than the {maxBytes / (1024 * 1024)} MB limit.");
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        await using var stream = file.OpenReadStream();
        await attachments.UploadAsync(
            form.Owner, form.OwnerId, stream, file.FileName, contentType, file.Length, form.Description, userId, ct);
        if (form.Owner == AttachmentOwner.Defect)
        {
            await notifications.NotifyDefectFileAddedAsync(form.OwnerId, userId, ct);
        }

        return wantsJson ? Results.Ok(new { ok = true }) : Results.LocalRedirect($"~{back}");
    }

    /// <summary>Most files that can be attached to a single comment.</summary>
    public const int MaxFilesPerComment = 10;

    /// <summary>The non-empty files posted with a comment form.</summary>
    public static IReadOnlyList<IFormFile> CommentFiles(IFormFileCollection? files) =>
        files is null ? [] : files.Where(f => f.Length > 0).ToList();

    /// <summary>
    /// Checks the optional files posted with a comment before the comment is saved. Returns an
    /// error message, or null when there are no files or they're all fine to attach.
    /// </summary>
    public static async Task<string?> ValidateCommentFilesAsync(
        IReadOnlyList<IFormFile> files, AttachmentService attachments, SystemSettingsService settings, CancellationToken ct)
    {
        if (files.Count == 0)
        {
            return null;
        }

        if (!attachments.StorageConfigured)
        {
            return "Attachment storage isn't configured, so the attachments couldn't be added.";
        }

        if (files.Count > MaxFilesPerComment)
        {
            return $"A comment can have at most {MaxFilesPerComment} attachments.";
        }

        var maxBytes = await settings.MaxUploadBytesAsync(ct);
        var tooLarge = files.FirstOrDefault(f => f.Length > maxBytes);
        return tooLarge is null
            ? null
            : $"{tooLarge.FileName} is larger than the {maxBytes / (1024 * 1024)} MB limit.";
    }

    /// <summary>
    /// Stores a comment's files. Returns null on success, or an error message if storage failed
    /// (the caller then removes the just-created comment, which also clears any files already stored).
    /// </summary>
    public static async Task<string?> AttachToCommentAsync(
        AttachmentOwner owner, Guid commentId, IReadOnlyList<IFormFile> files, string userId,
        AttachmentService attachments, ILogger logger, CancellationToken ct)
    {
        try
        {
            foreach (var file in files)
            {
                var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
                await using var stream = file.OpenReadStream();
                await attachments.UploadAsync(owner, commentId, stream, file.FileName, contentType, file.Length, null, userId, ct);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to store the files attached to comment {CommentId}.", commentId);
            return "The attachments couldn't be uploaded, so the comment wasn't added. Please try again.";
        }
    }

    private static IResult Fail(bool wantsJson, string back, string error) =>
        wantsJson
            ? Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest)
            : RedirectWithError(back, error);

    private static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal principal, AttachmentService attachments, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (await attachments.CanModifyAsync(id, userId, principal.IsInRole(Data.Roles.QA), ct))
        {
            await attachments.DeleteAsync(id, userId, ct);
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
