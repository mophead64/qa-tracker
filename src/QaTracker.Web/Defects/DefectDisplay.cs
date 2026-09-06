namespace QaTracker.Web.Defects;

/// <summary>Presentation helpers for defect enums.</summary>
public static class DefectDisplay
{
    public static string StatusLabel(DefectStatus status) => status switch
    {
        DefectStatus.NotFixed => "Not fixed",
        DefectStatus.Fixing => "Fixing",
        DefectStatus.ToCheck => "To check",
        DefectStatus.Fixed => "Fixed",
        DefectStatus.NotADefect => "Not a defect",
        _ => status.ToString(),
    };

    public static string StatusBadgeClass(DefectStatus status) => status switch
    {
        DefectStatus.NotFixed => "badge badge-red",
        DefectStatus.Fixing => "badge badge-blue",
        DefectStatus.ToCheck => "badge badge-amber",
        DefectStatus.Fixed => "badge badge-brand",
        DefectStatus.NotADefect => "badge badge-gray",
        _ => "badge badge-gray",
    };

    /// <summary>Solid, fully colour-filled chip for the editable status control on the detail page.</summary>
    public static string StatusChipClass(DefectStatus status) => status switch
    {
        DefectStatus.NotFixed => "chip chip-red",
        DefectStatus.Fixing => "chip chip-blue",
        DefectStatus.ToCheck => "chip chip-amber",
        DefectStatus.Fixed => "chip chip-brand",
        DefectStatus.NotADefect => "chip chip-gray",
        _ => "chip chip-gray",
    };

    public static string SeverityLabel(DefectSeverity severity) => severity switch
    {
        DefectSeverity.Low => "Low",
        DefectSeverity.Medium => "Medium",
        DefectSeverity.High => "High",
        DefectSeverity.Critical => "Critical",
        _ => severity.ToString(),
    };

    public static string SeverityBadgeClass(DefectSeverity severity) => severity switch
    {
        DefectSeverity.Low => "badge badge-gray",
        DefectSeverity.Medium => "badge badge-blue",
        DefectSeverity.High => "badge badge-amber",
        DefectSeverity.Critical => "badge badge-red",
        _ => "badge badge-gray",
    };

    /// <summary>Solid, fully colour-filled chip for the editable severity control on the detail page.</summary>
    public static string SeverityChipClass(DefectSeverity severity) => severity switch
    {
        DefectSeverity.Low => "chip chip-gray",
        DefectSeverity.Medium => "chip chip-blue",
        DefectSeverity.High => "chip chip-amber",
        DefectSeverity.Critical => "chip chip-red",
        _ => "chip chip-gray",
    };

    /// <summary>Human-friendly reference, e.g. "D-3".</summary>
    public static string Ref(int number) => $"D-{number}";
}
