namespace QaTracker.Web.Defects;

/// <summary>
/// How serious a defect is. Ordered low → high so <c>OrderByDescending</c> puts the most
/// serious first. New defects default to <see cref="Medium"/>.
/// </summary>
public enum DefectSeverity
{
    Low,
    Medium,
    High,
    Critical,
}
