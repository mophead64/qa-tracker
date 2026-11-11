namespace QaTracker.Web.Components.Shared;

/// <summary>
/// One entry in a page's "On this page" contents rail (<see cref="PageContents"/>):
/// <paramref name="Id"/> is the <c>id</c> of the element to jump to, <paramref name="Label"/>
/// the link text, and <paramref name="Count"/> an optional number shown beside it (hidden when
/// null or zero) — e.g. how many comments or defects the section holds.
/// </summary>
public sealed record PageSection(string Id, string Label, int? Count = null)
{
    /// <summary>
    /// A stable, URL-friendly id from a heading — "Other open defects" -> "other-open-defects".
    /// </summary>
    public static string Slug(string text)
    {
        var chars = text.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
