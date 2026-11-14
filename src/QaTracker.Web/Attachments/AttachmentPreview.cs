namespace QaTracker.Web.Attachments;

/// <summary>How an attachment can be shown in the in-app preview modal.</summary>
public enum PreviewKind
{
    None,
    Image,
    Video,
    Audio,
    Pdf,
}

/// <summary>
/// Which attachments can be previewed in the app, decided by file extension against an allow-list.
/// The preview endpoint serves them with the <em>canonical</em> content type for that extension —
/// never the type the uploader's browser claimed — plus <c>nosniff</c>, so a renamed file can't be
/// served as something the browser would execute. SVG is deliberately excluded (it can carry
/// script); everything not listed stays download-only.
/// </summary>
public static class AttachmentPreview
{
    private static readonly Dictionary<string, (PreviewKind Kind, string ContentType)> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = (PreviewKind.Image, "image/png"),
            [".jpg"] = (PreviewKind.Image, "image/jpeg"),
            [".jpeg"] = (PreviewKind.Image, "image/jpeg"),
            [".gif"] = (PreviewKind.Image, "image/gif"),
            [".webp"] = (PreviewKind.Image, "image/webp"),
            [".bmp"] = (PreviewKind.Image, "image/bmp"),
            [".avif"] = (PreviewKind.Image, "image/avif"),
            [".mp4"] = (PreviewKind.Video, "video/mp4"),
            [".m4v"] = (PreviewKind.Video, "video/mp4"),
            [".webm"] = (PreviewKind.Video, "video/webm"),
            [".mov"] = (PreviewKind.Video, "video/quicktime"),
            [".ogv"] = (PreviewKind.Video, "video/ogg"),
            [".mp3"] = (PreviewKind.Audio, "audio/mpeg"),
            [".wav"] = (PreviewKind.Audio, "audio/wav"),
            [".ogg"] = (PreviewKind.Audio, "audio/ogg"),
            [".oga"] = (PreviewKind.Audio, "audio/ogg"),
            [".m4a"] = (PreviewKind.Audio, "audio/mp4"),
            [".aac"] = (PreviewKind.Audio, "audio/aac"),
            [".flac"] = (PreviewKind.Audio, "audio/flac"),
            [".pdf"] = (PreviewKind.Pdf, "application/pdf"),
        };

    public static PreviewKind KindOf(string? fileName) => Resolve(fileName)?.Kind ?? PreviewKind.None;

    public static bool CanPreview(string? fileName) => KindOf(fileName) != PreviewKind.None;

    /// <summary>The preview kind and the content type to serve it as, or null when not previewable.</summary>
    public static (PreviewKind Kind, string ContentType)? Resolve(string? fileName) =>
        Allowed.TryGetValue(Path.GetExtension(fileName ?? string.Empty), out var entry) ? entry : null;

    /// <summary>Lower-case name for the modal script's data-preview-kind.</summary>
    public static string KindName(PreviewKind kind) => kind.ToString().ToLowerInvariant();
}

/// <summary>
/// Everything the preview modal shows about one file: what to load, its name, who added it and
/// when, its description if it has one, and — for a file attached to a comment — that comment, for context. Rendered as
/// <c>data-preview-*</c> attributes on a trigger (<see cref="Attributes"/>), or as JSON in an
/// attachments panel's step-through list (property names match attachment-preview.js).
/// </summary>
public sealed record PreviewItem(
    string Kind, string Src, string Download, string Name, string? Meta,
    string? CommentAuthor = null, string? CommentWhen = null, string? CommentBody = null, string? CommentHref = null,
    string? Description = null, IReadOnlyList<PreviewTextRun>? CommentRuns = null)
{
    /// <summary>A previewable attachment; null when the file type has no preview.</summary>
    public static PreviewItem? For(Guid id, string fileName, string? addedBy, DateTimeOffset? addedUtc,
        string? commentAuthor = null, DateTimeOffset? commentUtc = null, string? commentBody = null, string? commentHref = null,
        string? description = null, IEnumerable<(string Text, bool IsMention)>? commentRuns = null)
    {
        var kind = AttachmentPreview.KindOf(fileName);
        if (kind == PreviewKind.None)
        {
            return null;
        }

        var meta = (addedBy, addedUtc) switch
        {
            ({ } who, { } when) => $"Added by {who} on {Timestamp(when)}",
            ({ } who, null) => $"Added by {who}",
            _ => null,
        };
        return new PreviewItem(AttachmentPreview.KindName(kind), $"attachments/{id}/preview", $"attachments/{id}", fileName, meta,
            commentAuthor, commentUtc is { } c ? Timestamp(c) : null, commentBody, commentHref,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            commentRuns?.Select(r => new PreviewTextRun(r.Text, r.IsMention)).ToList());
    }

    /// <summary>The trigger attributes attachment-preview.js reads (empty values left off).</summary>
    public IReadOnlyDictionary<string, object> Attributes
    {
        get
        {
            var attributes = new Dictionary<string, object>
            {
                ["data-preview"] = "",
                ["data-preview-kind"] = Kind,
                ["data-preview-src"] = Src,
                ["data-preview-download"] = Download,
                ["data-preview-name"] = Name,
            };
            void Add(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value)) attributes[key] = value;
            }
            Add("data-preview-meta", Meta);
            Add("data-preview-comment-author", CommentAuthor);
            Add("data-preview-comment-when", CommentWhen);
            Add("data-preview-comment-body", CommentBody);
            Add("data-preview-comment-href", CommentHref);
            Add("data-preview-description", Description);
            if (CommentRuns is { Count: > 0 })
            {
                attributes["data-preview-comment-runs"] = Json(CommentRuns);
            }
            return attributes;
        }
    }

    /// <summary>A list of items as the JSON a panel's <c>data-preview-sequence</c> carries.</summary>
    public static string Json(IEnumerable<PreviewItem> items) => Json<IEnumerable<PreviewItem>>(items);

    private static string Json<T>(T value) =>
        System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

    private static string Timestamp(DateTimeOffset when) =>
        when.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A run of a comment's text in the preview: plain, or an "@Name" mention shown as a green tag.</summary>
public sealed record PreviewTextRun(string Text, bool Mention);
