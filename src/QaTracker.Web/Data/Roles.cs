namespace QaTracker.Web.Data;

/// <summary>
/// Application roles. Seeded on startup and used to drive authorization policies.
/// </summary>
public static class Roles
{
    public const string QA = "QA";
    public const string Dev = "Dev";

    public static readonly IReadOnlyList<string> All = [QA, Dev];
}
