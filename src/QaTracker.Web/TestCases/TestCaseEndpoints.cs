using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;

namespace QaTracker.Web.TestCases;

/// <summary>
/// CSV export of a project's test cases, plus the form-post actions on the test-case
/// list and detail pages (both static-rendered): setting a result, linking defects, and
/// comments. Each action redirects back to where it was triggered.
/// </summary>
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

        // Blank starter template for the importer — headers plus a couple of illustrative rows.
        endpoints.MapGet("/projects/{projectId:guid}/test-cases/import/template.csv", () =>
        {
            var sb = new StringBuilder();
            sb.Append("Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink\n");
            sb.Append("Authentication,Functional,Users cannot log in after 3 failed attempts,Open the app,\n");
            sb.Append(",,,Enter the wrong password three times,\n");
            sb.Append(",,,Expect the account to be locked,D-1\n");
            sb.Append("Performance,Non-Functional,Search returns results within 500ms,,\n");

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "test-case-import-template.csv");
        }).RequireAuthorization();

        var tc = endpoints.MapGroup("/test-cases/{testCaseId:guid}").RequireAuthorization();

        // QA sets results and links defects.
        tc.MapPost("/result", async (Guid testCaseId, TestCaseService testCases, [FromForm] TestResult result, [FromForm] string? returnUrl) =>
        {
            await testCases.SetResultAsync(testCaseId, result);
            return LocalRedirect(returnUrl);
        }).RequireAuthorization(p => p.RequireRole(Roles.QA));

        // The "Link defects" modal's form: link every checked defect. [FromForm] can't bind
        // the checkbox array, so read the form directly; an IFormCollection parameter still
        // enforces antiforgery. Creating a *new* defect happens on the defect form instead
        // (its ?testCaseId= param prefills the defect and links it back here on save).
        tc.MapPost("/defects/link", async (Guid testCaseId, DefectService defects, IFormCollection form) =>
        {
            foreach (var raw in form["defectIds"])
            {
                if (Guid.TryParse(raw, out var defectId))
                {
                    await defects.LinkTestCaseAsync(defectId, testCaseId);
                }
            }
            return LocalRedirect(form["returnUrl"].ToString());
        }).RequireAuthorization(p => p.RequireRole(Roles.QA));

        tc.MapPost("/defects/unlink", async (Guid testCaseId, DefectService defects, [FromForm] Guid defectId, [FromForm] string? returnUrl) =>
        {
            await defects.UnlinkTestCaseAsync(defectId, testCaseId);
            return LocalRedirect(returnUrl);
        }).RequireAuthorization(p => p.RequireRole(Roles.QA));

        // Anyone authenticated can comment; delete is author-or-QA.
        tc.MapPost("/comments", async (Guid testCaseId, ClaimsPrincipal principal, TestCaseService testCases, [FromForm] string body, [FromForm] string? returnUrl) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(body))
            {
                await testCases.AddCommentAsync(testCaseId, userId, body);
            }
            return LocalRedirect(returnUrl);
        });

        tc.MapPost("/comments/{commentId:guid}/delete", async (Guid testCaseId, Guid commentId, ClaimsPrincipal principal, TestCaseService testCases, [FromForm] string? returnUrl) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var comment = (await testCases.ListCommentsAsync(testCaseId)).FirstOrDefault(c => c.Id == commentId);
            if (comment is not null && (principal.IsInRole(Roles.QA) || comment.AuthorId == userId))
            {
                await testCases.DeleteCommentAsync(commentId);
            }
            return LocalRedirect(returnUrl);
        });

        return endpoints;
    }

    // Redirect back to a same-site path only; fall back to the test-cases area on anything odd.
    private static IResult LocalRedirect(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? Results.LocalRedirect($"~{returnUrl}")
            : Results.LocalRedirect("~/");

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

    private static string Field(string value) => Csv.Field(value);

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        return slug.Length > 0 ? slug : "project";
    }
}
