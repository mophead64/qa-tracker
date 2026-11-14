namespace QaTracker.Web.Comments;

/// <summary>
/// Paging for the comment threads on the project dashboard, test case and defect pages.
/// Comments stay in posting order (oldest first), <see cref="PageSize"/> to a page, and with no
/// <c>?commentsPage</c> in the URL a thread opens on its <em>last</em> page — the newest
/// comments, sitting right above the "Add a comment" box — with "Older" links to page back.
/// </summary>
public static class CommentPaging
{
    public const int PageSize = 5;

    /// <summary>Query-string parameter holding the page number (1-based).</summary>
    public const string QueryParam = "commentsPage";

    /// <summary>Query-string parameter naming a comment to outline (the attachment preview's
    /// "Go to comment"); the page renders that comment with <see cref="HighlightClass"/>.</summary>
    public const string HighlightParam = "highlight";

    /// <summary>The outline a highlighted comment gets (app.css).</summary>
    public const string HighlightClass = "comment-highlight";

    /// <summary>Id of the comments card on each page — paging links and redirects land here.</summary>
    public const string Anchor = "comments";

    /// <summary>
    /// The slice of <paramref name="all"/> to show. <paramref name="requested"/> is the raw
    /// query value: missing or unparsable means the newest page; out-of-range numbers are
    /// clamped, so a page emptied by a delete falls back to the last one that still exists.
    /// </summary>
    public static CommentPage<T> Slice<T>(IReadOnlyList<T> all, string? requested)
    {
        var totalPages = Math.Max(1, (all.Count + PageSize - 1) / PageSize);
        var isExplicit = int.TryParse(requested, out var number);
        var current = isExplicit ? Math.Clamp(number, 1, totalPages) : totalPages;

        return new CommentPage<T>(
            all.Skip((current - 1) * PageSize).Take(PageSize).ToList(),
            current,
            totalPages,
            all.Count,
            isExplicit);
    }

    /// <summary>
    /// "?commentsPage=N" from a posted form value, for redirecting back to the page a comment
    /// action came from — empty when there isn't a valid one (the default, newest page).
    /// </summary>
    public static string QueryFrom(string? formValue) =>
        int.TryParse(formValue, out var page) && page > 0 ? $"?{QueryParam}={page}" : "";
}

/// <param name="Items">The comments on this page, oldest first.</param>
/// <param name="Current">This page's number (1-based).</param>
/// <param name="IsExplicit">Whether the URL asked for a page. When it didn't, actions taken
/// on the page (react, delete) return to the default newest page rather than pinning this
/// number — so the reader keeps seeing the latest comments as new ones push the page on.</param>
public sealed record CommentPage<T>(IReadOnlyList<T> Items, int Current, int TotalPages, int TotalItems, bool IsExplicit)
{
    /// <summary>The page number to post back with a comment action, or null for the default.</summary>
    public int? ReturnPage => IsExplicit ? Current : null;

    /// <summary>"?commentsPage=N" to keep the reader on this page, or "" for the default.</summary>
    public string ReturnQuery => IsExplicit ? $"?{CommentPaging.QueryParam}={Current}" : "";
}
