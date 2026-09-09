using System.Globalization;

namespace QaTracker.Web.Hosting.UpdateCheck;

/// <summary>
/// Loose version comparison for the CalVer scheme the project releases under
/// (<c>YYYY.MM.DD</c>, with a <c>.N</c> suffix for a second release the same day). Tags may
/// carry a leading <c>v</c> and a <c>-prerelease</c> / <c>+build</c> suffix, both ignored.
/// </summary>
public static class VersionComparison
{
    /// <summary>
    /// True when <paramref name="candidate"/> is a strictly newer version than
    /// <paramref name="current"/>. If either value can't be parsed into dotted integers the
    /// answer is <c>false</c> — an unparseable version never triggers an "update available".
    /// </summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out var candidateParts) || !TryParse(current, out var currentParts))
        {
            return false;
        }

        var length = Math.Max(candidateParts.Length, currentParts.Length);
        for (var i = 0; i < length; i++)
        {
            var a = i < candidateParts.Length ? candidateParts[i] : 0;
            var b = i < currentParts.Length ? currentParts[i] : 0;
            if (a != b)
            {
                return a > b;
            }
        }

        return false;
    }

    private static bool TryParse(string? value, out int[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        // Drop a "-prerelease" or "+buildmetadata" suffix.
        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        var segments = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var result = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
            {
                return false;
            }
        }

        parts = result;
        return true;
    }
}
