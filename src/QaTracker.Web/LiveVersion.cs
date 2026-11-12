using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QaTracker.Web;

/// <summary>
/// Short fingerprints of what a "live" page shows (see live-page.js). A page renders its current
/// stamp; the browser polls a cheap endpoint returning the stamp as it is now and only re-fetches
/// the page when the two differ — so an idle page costs a few small queries per poll, not a render.
/// </summary>
public static partial class LiveVersion
{
    /// <summary>Default seconds between polls; QATRACKER_LIVE_POLL_SECONDS overrides it.</summary>
    public const int DefaultPollSeconds = 10;

    public static int PollSeconds(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue("QATRACKER_LIVE_POLL_SECONDS", DefaultPollSeconds), 2, 3600);

    /// <summary>
    /// Whether a request is a live page's version poll (/projects/{p}/test-cases/live or
    /// /projects/{p}/test-cases/{t}/live). Every open live tab sends one every few seconds, so —
    /// like the notification feed — they're kept out of the request log and traces.
    /// </summary>
    public static bool IsPollPath(PathString path) => path.HasValue && PollPath().IsMatch(path.Value!);

    [GeneratedRegex(@"^/projects/[0-9a-fA-F-]{36}/test-cases(/[0-9a-fA-F-]{36})?/live$")]
    private static partial Regex PollPath();

    /// <summary>A stable 16-hex-digit hash of the parts, in order.</summary>
    public static string Of(params object?[] parts)
    {
        var text = new StringBuilder();
        foreach (var part in parts)
        {
            text.Append(part switch
            {
                null => "∅",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                System.Collections.IEnumerable list and not string => string.Join(",", list.Cast<object?>().Select(i => Of(i))),
                _ => part.ToString(),
            });
            text.Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16].ToLowerInvariant();
    }
}
