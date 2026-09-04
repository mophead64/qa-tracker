namespace QaTracker.Web.Defects;

/// <summary>
/// Where a defect sits in the fix workflow. New defects start <see cref="NotFixed"/>;
/// a dev moves it through <see cref="Fixing"/> / <see cref="Fixed"/>, and a QA verifies
/// via <see cref="ToCheck"/> or closes it as <see cref="NotADefect"/>.
/// </summary>
public enum DefectStatus
{
    NotFixed,
    Fixing,
    ToCheck,
    Fixed,
    NotADefect,
}
