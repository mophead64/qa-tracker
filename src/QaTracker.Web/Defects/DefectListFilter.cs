namespace QaTracker.Web.Defects;

/// <summary>Which defects the defects page lists. Stored per user (like
/// <see cref="TestCases.TestCaseListFilter"/>), so the choice follows them across projects and logins.</summary>
public enum DefectListFilter
{
    /// <summary>Every defect. The default.</summary>
    All = 0,

    /// <summary>Everything not closed — the page's To verify / To fix / Unassigned / Other open groups.</summary>
    Open = 1,

    /// <summary>Fixed or Not a defect — the page's Closed group.</summary>
    Closed = 2,

    NotFixed = 3,
    Fixing = 4,
    ToCheck = 5,
    Fixed = 6,
    NotADefect = 7,
}

public static class DefectListFilters
{
    /// <summary>In dropdown order: All, the two groups, then each status in workflow order.</summary>
    public static readonly IReadOnlyList<DefectListFilter> Ordered =
    [
        DefectListFilter.All,
        DefectListFilter.Open,
        DefectListFilter.Closed,
        DefectListFilter.NotFixed,
        DefectListFilter.Fixing,
        DefectListFilter.ToCheck,
        DefectListFilter.Fixed,
        DefectListFilter.NotADefect,
    ];

    /// <summary>The first single-status option (the dropdown puts a divider above it).</summary>
    public const DefectListFilter FirstStatus = DefectListFilter.NotFixed;

    public static string Label(DefectListFilter filter) => filter switch
    {
        DefectListFilter.All => "All defects",
        DefectListFilter.Open => "Open",
        DefectListFilter.Closed => "Closed",
        _ => StatusOf(filter) is { } status ? DefectDisplay.StatusLabel(status) : filter.ToString(),
    };

    /// <summary>A short note under the grouped options.</summary>
    public static string? Hint(DefectListFilter filter) => filter switch
    {
        DefectListFilter.Open => "Not fixed, fixing or to check",
        DefectListFilter.Closed => "Fixed or not a defect",
        _ => null,
    };

    public static bool Matches(this DefectListFilter filter, DefectStatus status) => filter switch
    {
        DefectListFilter.All => true,
        DefectListFilter.Open => !IsClosed(status),
        DefectListFilter.Closed => IsClosed(status),
        _ => StatusOf(filter) == status,
    };

    private static bool IsClosed(DefectStatus status) => status is DefectStatus.Fixed or DefectStatus.NotADefect;

    private static DefectStatus? StatusOf(DefectListFilter filter) => filter switch
    {
        DefectListFilter.NotFixed => DefectStatus.NotFixed,
        DefectListFilter.Fixing => DefectStatus.Fixing,
        DefectListFilter.ToCheck => DefectStatus.ToCheck,
        DefectListFilter.Fixed => DefectStatus.Fixed,
        DefectListFilter.NotADefect => DefectStatus.NotADefect,
        _ => null,
    };
}
