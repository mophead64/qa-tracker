namespace QaTracker.Web.Hosting;

/// <summary>
/// Permanent redirects from the URL shapes used before the November 2026 routing clean-up (see
/// <see cref="AppUrls"/>), so bookmarks, shared links and old notification links keep working.
/// The old test case address carried its scope — the new one doesn't, so the scope segment is
/// simply dropped. Query strings (e.g. ?fromDefect=, ?results=) are carried across.
/// </summary>
public static class LegacyRoutes
{
    private const string Old = "/projects/{projectId:guid}/test-cases";

    public static IEndpointRouteBuilder MapLegacyRedirects(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        Redirect(endpoints, $"{Old}/scopes/{{scopeId:guid}}/cases/{{testCaseId:guid}}",
            v => AppUrls.TestCase(v.Guid("projectId"), v.Guid("testCaseId")));
        Redirect(endpoints, $"{Old}/scopes/{{scopeId:guid}}/cases/{{testCaseId:guid}}/edit",
            v => AppUrls.EditTestCase(v.Guid("projectId"), v.Guid("testCaseId")));
        Redirect(endpoints, $"{Old}/scopes/{{scopeId:guid}}/cases/new",
            v => AppUrls.NewTestCase(v.Guid("projectId"), v.Guid("scopeId")));
        Redirect(endpoints, $"{Old}/scopes/new",
            v => AppUrls.NewScope(v.Guid("projectId")));
        Redirect(endpoints, $"{Old}/scopes/{{scopeId:guid}}",
            v => AppUrls.Scope(v.Guid("projectId"), v.Guid("scopeId")));
        Redirect(endpoints, $"{Old}/scopes/{{scopeId:guid}}/edit",
            v => AppUrls.EditScope(v.Guid("projectId"), v.Guid("scopeId")));
        Redirect(endpoints, "/projects/{projectId:guid}/test-cases.csv",
            v => AppUrls.ExportTestCases(v.Guid("projectId")));

        return endpoints;
    }

    private static void Redirect(IEndpointRouteBuilder endpoints, string pattern, Func<RouteValueDictionary, string> target) =>
        endpoints.MapGet(pattern, (HttpContext http) =>
            Results.Redirect(AppUrls.Absolute(target(http.Request.RouteValues)) + http.Request.QueryString, permanent: true));

    private static Guid Guid(this RouteValueDictionary values, string key) => System.Guid.Parse(values[key]!.ToString()!);
}
