namespace QaTracker.Web.Data;

/// <summary>
/// Application roles. These are seeded on startup and will drive permissions in later phases.
/// </summary>
public static class Roles
{
    public const string QA = "QA";
    public const string Dev = "Dev";

    public static readonly IReadOnlyList<string> All = [QA, Dev];
}
