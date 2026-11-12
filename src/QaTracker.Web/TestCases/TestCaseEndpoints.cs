using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Auth;
using QaTracker.Web.Comments;
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

        // Export in the same column shape the importer accepts, so a plan can be exported,
        // bulk-edited and re-imported (see TestCaseCsv). ?results=true adds Result / Tested by /
        // Tested columns — the importer ignores them, so that file re-imports too.
        endpoints.MapGet("/projects/{projectId:guid}/test-cases/export.csv", async (
            Guid projectId,
            ProjectService projects,
            TestScopeService scopes,
            DefectService defects,
            [FromQuery] bool? results) =>
        {
            var project = await projects.GetAsync(projectId);
            if (project is null)
            {
                return Results.NotFound();
            }

            var plan = await scopes.ListForProjectAsync(projectId);
            var defectNumbersByCase = await TestCaseCsv.DefectNumbersByCaseAsync(defects, projectId);
            var withResults = results == true;
            var csv = TestCaseCsv.Export(plan, defectNumbersByCase, withResults);
            var fileName = $"{Slug(project.Name)}-test-cases{(withResults ? "-with-results" : "")}.csv";

            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
        }).RequireAuthorization();

        // The live test plan / scope pages' version stamp (see live-page.js).
        endpoints.MapGet("/projects/{projectId:guid}/test-cases/live", async (
            Guid projectId, HttpContext http, TestScopeService scopes, CancellationToken ct) =>
            LiveResult(http, await scopes.GetPlanLiveVersionAsync(projectId, ct))).RequireAuthorization();

        // Blank starter template for the importer — headers plus a couple of illustrative rows.
        endpoints.MapGet("/projects/{projectId:guid}/test-cases/import/template.csv", () =>
        {
            var sb = new StringBuilder();
            sb.Append(TestCaseCsv.Header).Append('\n');
            sb.Append("Authentication,Functional,Users cannot log in after 3 failed attempts,Open the app,\n");
            sb.Append(",,,Enter the wrong password three times,\n");
            sb.Append(",,,Expect the account to be locked,D-1\n");
            sb.Append("Performance,Non-Functional,Search returns results within 500ms,,\n");

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "test-case-import-template.csv");
        }).RequireAuthorization();

        // The "Reorder scopes" modal on the test cases page: one scopeIds field per scope, in
        // the new order. Read the form directly — [FromForm] can't bind the repeated field.
        endpoints.MapPost("/projects/{projectId:guid}/scopes/order", async (
            Guid projectId, TestScopeService scopes, IFormCollection form, CancellationToken ct) =>
        {
            var ids = form["scopeIds"]
                .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList();
            await scopes.ReorderAsync(projectId, ids, ct);
            return LocalRedirect(AppUrls.Absolute(AppUrls.TestPlan(projectId)));
        }).RequireAuthorization(Policies.ManageTestCases);

        // Actions on one test case, addressed under its project like the case's own page. A case
        // that isn't in that project is a 404 for every action (the filter below).
        var tc = endpoints.MapGroup("/projects/{projectId:guid}/test-cases/{testCaseId:guid}")
            .RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                var values = context.HttpContext.Request.RouteValues;
                if (!Guid.TryParse(values["projectId"]?.ToString(), out var projectId)
                    || !Guid.TryParse(values["testCaseId"]?.ToString(), out var testCaseId)
                    || !await context.HttpContext.RequestServices.GetRequiredService<TestCaseService>()
                        .BelongsToProjectAsync(testCaseId, projectId, context.HttpContext.RequestAborted))
                {
                    return Results.NotFound();
                }
                return await next(context);
            });

        // The live test case page's version stamp (live-page.js polls it, and re-fetches the page
        // only when it changes). A case that's gone is a 404 (the filter above).
        tc.MapGet("/live", async (Guid testCaseId, HttpContext http, TestCaseService testCases, CancellationToken ct) =>
            await testCases.GetLiveVersionAsync(testCaseId, ct) is { } version
                ? LiveResult(http, version)
                : Results.NotFound());

        // The History modal's body, fetched when the modal opens (see TestCaseHistoryPanel).
        tc.MapGet("/history", (Guid testCaseId) =>
            new Microsoft.AspNetCore.Http.HttpResults.RazorComponentResult<Components.Shared.TestCaseHistoryPanel>(
                new { TestCaseId = testCaseId }));

        // QA sets results and links defects.
        tc.MapPost("/result", async (Guid testCaseId, ClaimsPrincipal principal, TestCaseService testCases, [FromForm] TestResult result, [FromForm] string? returnUrl) =>
        {
            await testCases.SetResultAsync(testCaseId, result, principal.FindFirstValue(ClaimTypes.NameIdentifier));
            return LocalRedirect(returnUrl);
        }).RequireAuthorization(p => p.RequireRole(Roles.QA));

        // The "Link or create defect" modal's form: link every checked defect. [FromForm] can't bind
        // the checkbox array, so read the form directly; an IFormCollection parameter still
        // enforces antiforgery. Creating a *new* defect happens on the defect form instead
        // (its ?testCaseId= param prefills the defect and links it back here on save).
        tc.MapPost("/defects/link", async (Guid testCaseId, ClaimsPrincipal principal, DefectService defects, IFormCollection form) =>
        {
            foreach (var raw in form["defectIds"])
            {
                if (Guid.TryParse(raw, out var defectId))
                {
                    await defects.LinkTestCaseAsync(defectId, testCaseId, principal.FindFirstValue(ClaimTypes.NameIdentifier));
                }
            }
            return LocalRedirect(form["returnUrl"].ToString());
        }).RequireAuthorization(p => p.RequireRole(Roles.QA));

        tc.MapPost("/defects/unlink", async (Guid testCaseId, ClaimsPrincipal principal, DefectService defects, [FromForm] Guid defectId, [FromForm] string? returnUrl) =>
        {
            await defects.UnlinkTestCaseAsync(defectId, testCaseId, principal.FindFirstValue(ClaimTypes.NameIdentifier));
            return LocalRedirect(returnUrl);
        }).RequireAuthorization(p => p.RequireRole(Roles.QA));

        // Anyone authenticated can comment; delete is author-or-QA.
        // Multipart: the comment text plus optional files (stored as attachments on the comment).
        tc.MapPost("/comments", async (
            Guid testCaseId, ClaimsPrincipal principal, TestCaseService testCases,
            AttachmentService attachments, SystemSettingsService settings, ILoggerFactory loggerFactory,
            [FromForm] string body, HttpRequest request, [FromForm] string? returnUrl, IFormFileCollection files, CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(body))
            {
                return LocalRedirect(returnUrl);
            }

            var uploads = AttachmentEndpoints.CommentFiles(files);
            var error = await AttachmentEndpoints.ValidateCommentFilesAsync(uploads, attachments, settings, ct);
            Guid? commentId = null;
            if (error is null)
            {
                commentId = await testCases.AddCommentAsync(testCaseId, userId, body, request.Form["mentions"].OfType<string>().ToList(), ct);
                if (uploads.Count > 0)
                {
                    error = await AttachmentEndpoints.AttachToCommentAsync(
                        AttachmentOwner.TestCaseComment, commentId.Value, uploads, userId, attachments,
                        loggerFactory.CreateLogger("QaTracker.Web.TestCases.Comments"), ct);
                    if (error is not null)
                    {
                        await testCases.DeleteCommentAsync(commentId.Value, ct);
                    }
                }
            }

            // Success lands on the new comment itself (the comment form's returnUrl carries no
            // page, so that's the default newest comments page, where it is); an error lands by
            // the form.
            return error is null
                ? LocalRedirect(returnUrl, fragment: $"comment-{commentId}")
                : LocalRedirect(returnUrl, error, CommentPaging.Anchor);
        });

        tc.MapPost("/comments/{commentId:guid}/delete", async (Guid testCaseId, Guid commentId, ClaimsPrincipal principal, TestCaseService testCases, [FromForm] string? returnUrl) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var comment = (await testCases.ListCommentsAsync(testCaseId)).FirstOrDefault(c => c.Id == commentId);
            if (comment is not null && (principal.IsInRole(Roles.QA) || comment.AuthorId == userId))
            {
                await testCases.DeleteCommentAsync(commentId);
            }
            // returnUrl carries the comments page the delete came from (see TestCaseDetail).
            return LocalRedirect(returnUrl, fragment: CommentPaging.Anchor);
        });

        return endpoints;
    }

    // Redirect back to a same-site path only; fall back to the test-cases area on anything odd.
    // fragment (no '#') is an element id to land on, replacing any fragment returnUrl had.
    private static IResult LocalRedirect(string? returnUrl, string? commentError = null, string? fragment = null)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return Results.LocalRedirect("~/");
        }

        var hash = string.IsNullOrEmpty(fragment) ? "" : $"#{fragment}";
        if (hash.Length > 0)
        {
            returnUrl = returnUrl.Split('#')[0];
        }

        if (commentError is null)
        {
            return Results.LocalRedirect($"~{returnUrl}{hash}");
        }

        var separator = returnUrl.Contains('?') ? '&' : '?';
        return Results.LocalRedirect($"~{returnUrl}{separator}commentError={Uri.EscapeDataString(commentError)}{hash}");
    }

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        return slug.Length > 0 ? slug : "project";
    }

    private static IResult LiveResult(HttpContext http, string version)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { version });
    }
}
