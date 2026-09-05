namespace QaTracker.Web.Telemetry;

/// <summary>
/// Recognises requests for static assets (CSS, JS, images, fonts, the Blazor framework
/// files and the SignalR circuit endpoint) so they can be kept out of traces — the
/// telemetry signal should be dominated by real page and API activity, not asset fetches.
/// </summary>
public static class StaticAssetFilter
{
    private static readonly string[] PrefixesToSkip =
    [
        "/_framework/", "/_content/", "/_blazor", "/_vs/", "/lib/",
    ];

    private static readonly HashSet<string> ExtensionsToSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".mjs", ".map", ".json", ".ico", ".png", ".jpg", ".jpeg", ".gif",
        ".svg", ".webp", ".avif", ".woff", ".woff2", ".ttf", ".eot", ".otf",
    };

    /// <summary>True when <paramref name="path"/> looks like a static asset request.</summary>
    /// <remarks>Accepts a <c>string</c> so it stays trivially unit-testable; an ASP.NET
    /// <c>PathString</c> converts implicitly at the call site.</remarks>
    public static bool IsStaticAsset(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (Array.Exists(PrefixesToSkip, prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var lastSegment = path.AsSpan(path.LastIndexOf('/') + 1);
        var dot = lastSegment.LastIndexOf('.');
        return dot >= 0 && ExtensionsToSkip.Contains(lastSegment[dot..].ToString());
    }
}
