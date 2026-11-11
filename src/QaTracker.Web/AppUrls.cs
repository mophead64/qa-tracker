namespace QaTracker.Web;

/// <summary>
/// The app's URLs, in one place. Resources nest under their project, and every resource is
/// addressed by its own id — a test case's address doesn't include its scope, so it never changes
/// when the case is moved:
/// <code>
/// /projects/{p}                          dashboard
/// /projects/{p}/test-cases               the test plan (every scope)
/// /projects/{p}/test-cases/{t}           a test case        (…/edit, and POST actions under it)
/// /projects/{p}/scopes/{s}               a scope's page     (…/edit, …/test-cases/new)
/// /projects/{p}/defects/{d}              a defect           (…/edit, and POST actions under it)
/// /preferences/…                         the signed-in user's saved list filters / layout
/// </code>
/// Paths are relative (no leading slash) so they resolve against <c>&lt;base href&gt;</c> in
/// markup; use <see cref="Absolute"/> for redirects and returnUrl values. The pre-November-2026
/// shapes (/test-cases/scopes/{s}/cases/{t} …) permanently redirect here — see LegacyRoutes.
/// </summary>
public static class AppUrls
{
    public static string Project(Guid projectId) => $"projects/{projectId}";

    // Test plan and test cases.
    public static string TestPlan(Guid projectId) => $"projects/{projectId}/test-cases";
    public static string TestCase(Guid projectId, Guid testCaseId) => $"projects/{projectId}/test-cases/{testCaseId}";
    public static string EditTestCase(Guid projectId, Guid testCaseId) => $"{TestCase(projectId, testCaseId)}/edit";
    /// <summary>POST target for a test case action, e.g. "result", "comments", "defects/link".</summary>
    public static string TestCaseAction(Guid projectId, Guid testCaseId, string action) => $"{TestCase(projectId, testCaseId)}/{action}";
    public static string ImportTestCases(Guid projectId) => $"projects/{projectId}/test-cases/import";
    public static string ImportTemplate(Guid projectId) => $"projects/{projectId}/test-cases/import/template.csv";
    public static string ExportTestCases(Guid projectId, bool withResults = false) =>
        $"projects/{projectId}/test-cases/export.csv{(withResults ? "?results=true" : "")}";

    // Scopes.
    public static string Scope(Guid projectId, Guid scopeId) => $"projects/{projectId}/scopes/{scopeId}";
    public static string NewScope(Guid projectId) => $"projects/{projectId}/scopes/new";
    public static string EditScope(Guid projectId, Guid scopeId) => $"{Scope(projectId, scopeId)}/edit";
    public static string NewTestCase(Guid projectId, Guid scopeId) => $"{Scope(projectId, scopeId)}/test-cases/new";
    public static string ReorderScopes(Guid projectId) => $"projects/{projectId}/scopes/order";

    // Defects.
    public static string Defects(Guid projectId) => $"projects/{projectId}/defects";
    public static string Defect(Guid projectId, Guid defectId) => $"projects/{projectId}/defects/{defectId}";

    // The signed-in user's saved preferences (POST).
    public const string TestCaseFilterPreference = "preferences/test-case-filter";
    public const string TestCaseLayoutPreference = "preferences/test-case-layout";
    public const string DefectFilterPreference = "preferences/defect-filter";
    public const string ProjectFilterPreference = "preferences/project-filter";

    /// <summary>Site-root-absolute form ("/projects/…") for redirects and returnUrl values.</summary>
    public static string Absolute(string relative) => "/" + relative.TrimStart('/');
}
