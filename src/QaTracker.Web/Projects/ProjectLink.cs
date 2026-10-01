using System.ComponentModel.DataAnnotations;

namespace QaTracker.Web.Projects;

/// <summary>
/// A user-defined link on a project (e.g. repo, staging environment, spec doc),
/// rendered as a labelled button on the project dashboard.
/// </summary>
public class ProjectLink
{
    public const int MaxLabelLength = 100;

    public const int MaxUrlLength = 2048;

    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    [Required]
    [MaxLength(MaxLabelLength)]
    public string Label { get; set; } = string.Empty;

    [Required]
    [MaxLength(MaxUrlLength)]
    public string Url { get; set; } = string.Empty;

    /// <summary>Position of the link within the project, ascending.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Why a (trimmed) label + URL can't be saved as a link, or null if they can: both are
    /// required, within the column lengths, and the URL must be absolute http(s) or mailto.
    /// Shared by the project form and the dashboard's quick "Add link".
    /// </summary>
    public static string? Validate(string label, string url)
    {
        if (label.Length == 0 || url.Length == 0)
        {
            return "Each link needs both a label and a URL.";
        }

        if (label.Length > MaxLabelLength)
        {
            return $"A link label can be at most {MaxLabelLength} characters.";
        }

        if (url.Length > MaxUrlLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return $"\"{url}\" is not a valid http(s) or mailto URL.";
        }

        return null;
    }
}
