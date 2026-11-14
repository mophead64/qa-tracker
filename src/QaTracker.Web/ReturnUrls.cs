namespace QaTracker.Web;

/// <summary>
/// The one check every endpoint uses on a posted <c>returnUrl</c> before redirecting to it: a
/// same-site path only. "//host" and "/\host" are both read by browsers as another site, and
/// ASP.NET's LocalRedirect refuses them by throwing (a 500), so they — and anything with control
/// characters — fall back instead.
/// </summary>
public static class ReturnUrls
{
    public static bool IsLocal(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(char.IsControl);

    /// <summary><paramref name="url"/> when it's a same-site path, else <paramref name="fallback"/>.</summary>
    public static string LocalOr(string? url, string fallback = "/") => IsLocal(url) ? url! : fallback;
}
