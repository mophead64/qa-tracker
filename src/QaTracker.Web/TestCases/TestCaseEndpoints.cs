using System.Globalization;
using System.Text;
using QaTracker.Web.Projects;

namespace QaTracker.Web.TestCases;

/// <summary>CSV export of a project's test cases (for documentation hand-off).</summary>
public static class TestCaseEndpoints
{
    public static IEndpointRouteBuilder MapTestCaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/projects/{projectId:guid}/test-cases.csv", async (
            Guid projectId,
            ProjectService projects,
            TestScopeService scopes) =>
        {
            var project = await projects.GetAsync(projectId);
            if (project is null)
            {
                return Results.NotFound();
            }

            var csv = BuildCsv(await scopes.ListForProjectAsync(projectId));
            var fileName = $"{Slug(project.Name)}-test-cases.csv";

            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
        }).RequireAuthorization();

        return endpoints;
    }

    private static string BuildCsv(IReadOnlyList<TestScope> scopes)
    {
        var sb = new StringBuilder();
        sb.Append("Kind,Scope,Scenario,Steps,Result,Created (UTC)\n");

        foreach (var scope in scopes)
        {
            foreach (var tc in scope.Cases)
            {
                sb.Append(Field(TestCaseDisplay.KindLabel(scope.Kind))).Append(',')
                  .Append(Field(scope.Name)).Append(',')
                  .Append(Field(tc.Scenario)).Append(',')
                  .Append(Field(tc.Steps ?? string.Empty)).Append(',')
                  .Append(Field(TestCaseDisplay.ResultLabel(tc.Result))).Append(',')
                  .Append(Field(tc.CreatedUtc.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)))
                  .Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string Field(string value)
    {
        var needsQuoting = value.AsSpan().IndexOfAny("\",\r\n") >= 0;
        var escaped = value.Replace("\"", "\"\"");
        return needsQuoting ? $"\"{escaped}\"" : escaped;
    }

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        return slug.Length > 0 ? slug : "project";
    }
}
