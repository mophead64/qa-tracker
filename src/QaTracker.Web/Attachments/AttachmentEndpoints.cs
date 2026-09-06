using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using QaTracker.Web.Admin;

namespace QaTracker.Web.Attachments;

/// <summary>
/// Attachment download proxy (the app never links directly to the storage account), plus
/// the static-rendered upload and delete form posts used by <c>AttachmentPanel</c>.
/// Every file is served as a download — nothing renders inline in the browser.
/// </summary>
public static class AttachmentEndpoints
{
    /// <summary>Text fields of the upload form (the file itself binds separately).</summary>
    public sealed record UploadForm(AttachmentOwner Owner, Guid OwnerId, string? Description, string? ReturnUrl);

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/attachments/{id:guid}", DownloadAsync).RequireAuthorization();

        var group = endpoints.MapGroup("/attachments").RequireAuthorization();
        group.MapPost("/upload", UploadAsync);
        group.MapPost("/{id:guid}/delete", DeleteAsync);

        return endpoints;
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
        [FromForm] UploadForm form,
        IFormFile? file,
        CancellationToken ct)
    {
        // The "Add files" modal (attachment-upload.js) posts one file per request with this
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

        return wantsJson ? Results.Ok(new { ok = true }) : Results.LocalRedirect($"~{back}");
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
