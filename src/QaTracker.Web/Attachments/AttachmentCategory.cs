namespace QaTracker.Web.Attachments;

/// <summary>A broad kind of file, for filtering an attachments list.</summary>
public enum AttachmentCategory
{
    Image,
    Video,
    Audio,
    Document,
    Other,
}

/// <summary>
/// Sorts attachments into <see cref="AttachmentCategory"/>s for the attachments filter. Decided by
/// file extension first (what people recognise), then by the content type the upload carried. Purely
/// for display — unlike <see cref="AttachmentPreview"/>, nothing is served or trusted on the strength of it.
/// </summary>
public static class AttachmentCategories
{
    /// <summary>The filter's options, in menu order (All comes first, as no filter).</summary>
    public static readonly IReadOnlyList<AttachmentCategory> All =
        [AttachmentCategory.Image, AttachmentCategory.Video, AttachmentCategory.Audio, AttachmentCategory.Document, AttachmentCategory.Other];

    private static readonly Dictionary<string, AttachmentCategory> ByExtension = Build(
        (AttachmentCategory.Image, ["png", "jpg", "jpeg", "gif", "webp", "bmp", "avif", "svg", "heic", "heif", "tif", "tiff", "ico"]),
        (AttachmentCategory.Video, ["mp4", "m4v", "webm", "mov", "ogv", "avi", "mkv", "wmv", "mpg", "mpeg", "3gp"]),
        (AttachmentCategory.Audio, ["mp3", "wav", "ogg", "oga", "m4a", "aac", "flac", "opus", "wma", "aiff"]),
        (AttachmentCategory.Document,
        [
            "pdf", "doc", "docx", "odt", "rtf", "txt", "md", "xls", "xlsx", "ods", "csv", "tsv",
            "ppt", "pptx", "odp", "key", "pages", "numbers", "html", "htm", "json", "xml", "yaml", "yml", "log",
        ]));

    public static AttachmentCategory Of(string? fileName, string? contentType = null)
    {
        var extension = Path.GetExtension(fileName ?? "").TrimStart('.');
        if (ByExtension.TryGetValue(extension, out var category))
        {
            return category;
        }

        var type = contentType?.Trim().ToLowerInvariant() ?? "";
        return type switch
        {
            _ when type.StartsWith("image/", StringComparison.Ordinal) => AttachmentCategory.Image,
            _ when type.StartsWith("video/", StringComparison.Ordinal) => AttachmentCategory.Video,
            _ when type.StartsWith("audio/", StringComparison.Ordinal) => AttachmentCategory.Audio,
            _ when type.StartsWith("text/", StringComparison.Ordinal) || type == "application/pdf"
                || type.Contains("officedocument", StringComparison.Ordinal) || type.Contains("opendocument", StringComparison.Ordinal)
                || type is "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint" or "application/rtf"
                => AttachmentCategory.Document,
            _ => AttachmentCategory.Other,
        };
    }

    /// <summary>The <c>?attachmentType=</c> value ("image", "video", "audio", "docs", "other").</summary>
    public static string Value(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Document => "docs",
        _ => category.ToString().ToLowerInvariant(),
    };

    /// <summary>Parses a <c>?attachmentType=</c> value; anything else (or nothing) is no filter.</summary>
    public static AttachmentCategory? Parse(string? value) =>
        All.Cast<AttachmentCategory?>().FirstOrDefault(c => string.Equals(Value(c!.Value), value?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Plural label for the menu and the empty message ("Images", "Docs").</summary>
    public static string Label(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Image => "Images",
        AttachmentCategory.Video => "Videos",
        AttachmentCategory.Audio => "Audio",
        AttachmentCategory.Document => "Docs",
        _ => "Other",
    };

    private static Dictionary<string, AttachmentCategory> Build(params (AttachmentCategory Category, string[] Extensions)[] groups) =>
        groups.SelectMany(g => g.Extensions.Select(e => (e, g.Category)))
            .ToDictionary(x => x.e, x => x.Category, StringComparer.OrdinalIgnoreCase);
}
