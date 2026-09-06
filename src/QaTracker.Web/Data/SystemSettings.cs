namespace QaTracker.Web.Data;

/// <summary>
/// The areas whose create/edit/delete access can be opened up to developers from the
/// system settings page. QA always has access; developers have it only when the matching
/// flag on <see cref="SystemSettings"/> is set.
/// </summary>
public enum ManageableArea
{
    Projects,
    TestCases,
    Defects,
}

/// <summary>
/// Single-row table (<see cref="Id"/> is always <see cref="SingletonId"/>) holding
/// instance-wide configuration an administrator can change at runtime: which areas
/// developers may manage (each flag defaults to <c>true</c>), and the per-file upload size
/// limit. The upload limit can't exceed the process ceiling
/// (<c>QATRACKER_MAX_UPLOAD_MB</c>, which also sizes the Kestrel request-body limit).
/// </summary>
public class SystemSettings
{
    public const int SingletonId = 1;

    /// <summary>Fallback per-file upload limit for a fresh database (also the seed default).</summary>
    public const int DefaultMaxUploadMb = 20;

    public int Id { get; set; } = SingletonId;

    public bool DevelopersCanManageProjects { get; set; } = true;

    public bool DevelopersCanManageTestCases { get; set; } = true;

    public bool DevelopersCanManageDefects { get; set; } = true;

    /// <summary>Per-file upload size limit, in MB. Clamped to [1, process ceiling] on save.</summary>
    public int MaxUploadMb { get; set; } = DefaultMaxUploadMb;

    public bool AllowsDeveloperManagementOf(ManageableArea area) => area switch
    {
        ManageableArea.Projects => DevelopersCanManageProjects,
        ManageableArea.TestCases => DevelopersCanManageTestCases,
        ManageableArea.Defects => DevelopersCanManageDefects,
        _ => false,
    };
}
