namespace QaTracker.Web.Data;

/// <summary>
/// Application roles. Seeded on startup and used to drive authorization policies. A user has
/// exactly one: <see cref="QA"/> (everything), <see cref="Dev"/> (fixes defects; may manage
/// content when System settings allow), or <see cref="Viewer"/> (reads everything, comments and
/// reacts, changes nothing else — and is never on a project team or assigned a defect).
/// </summary>
public static class Roles
{
    public const string QA = "QA";
    public const string Dev = "Dev";
    public const string Viewer = "Viewer";

    /// <summary>Every role, most privileged first (the role picker's order).</summary>
    public static readonly IReadOnlyList<string> All = [QA, Dev, Viewer];

    /// <summary>Roles that do the work: can be on a project team, be assigned defects, upload
    /// files and take part in the fix workflow (see <see cref="Auth.Policies.Contribute"/>).</summary>
    public static readonly IReadOnlyList<string> Contributors = [QA, Dev];

    /// <summary>Which role wins when a stale row or an SSO token carries more than one.</summary>
    public static string? MostPrivileged(IEnumerable<string?> roles)
    {
        var set = roles.OfType<string>().ToHashSet();
        return All.FirstOrDefault(set.Contains);
    }

    /// <summary>How the role reads in prose and pickers ("Developer" rather than "Dev").</summary>
    public static string Label(string? role) => role switch
    {
        Dev => "Developer",
        null or "" => "No role",
        _ => role,
    };
}
