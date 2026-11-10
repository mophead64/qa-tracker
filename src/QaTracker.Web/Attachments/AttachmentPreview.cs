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
