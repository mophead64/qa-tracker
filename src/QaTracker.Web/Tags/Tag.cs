using System.ComponentModel.DataAnnotations;
using QaTracker.Web.Projects;

namespace QaTracker.Web.Tags;

/// <summary>The badge colour a tag is shown in. Stored by name.</summary>
public enum TagColour
{
    Gray,
    Blue,
    Green,
    Amber,
    Orange,
    Red,
    Purple,
}

/// <summary>
/// A system-wide label for projects (tech stack, team, client, …). Created from any project's
/// dashboard or the System settings tags page, then offered on every project. Names are unique
/// ignoring case and spacing (<see cref="NormalizedName"/>).
/// </summary>
public class Tag
{
    public const int MaxNameLength = 40;

    public Guid Id { get; set; }

    [Required]
    [MaxLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="Name"/> upper-cased with its spacing collapsed — the unique key.</summary>
    [Required]
    [MaxLength(MaxNameLength)]
    public string NormalizedName { get; set; } = string.Empty;

    public TagColour Colour { get; set; } = TagColour.Gray;

    public DateTimeOffset CreatedUtc { get; set; }

    public List<Project> Projects { get; set; } = [];

    /// <summary>A tag name as typed, trimmed with runs of whitespace collapsed to one space.</summary>
    public static string Clean(string? name) =>
        string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string Normalize(string? name) => Clean(name).ToUpperInvariant();

    /// <summary>The problem with a (cleaned) tag name, or null when it's fine.</summary>
    public static string? Validate(string cleanedName) => cleanedName.Length switch
    {
        0 => "A tag needs a name.",
        > MaxNameLength => $"A tag name can be at most {MaxNameLength} characters.",
        _ => null,
    };
}
