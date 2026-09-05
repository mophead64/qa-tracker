using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Data;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Defects;

/// <summary>
/// Form-post endpoints for the actions on the defect detail page (status, assignee,
/// linking test cases, comments). The page is static-rendered; each control posts here
/// and we redirect back to it.
/// </summary>
public static class DefectEndpoints
{
    private const string BasePath = "/projects/{projectId:guid}/defects/{defectId:guid}";

    public static IEndpointRouteBuilder MapDefectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // QA-only: workflow, assignment and test-case linking.
        var qa = endpoints.MapGroup(BasePath).RequireAuthorization(p => p.RequireRole(Roles.QA));

        qa.MapPost("/status", async (Guid projectId, Guid defectId, DefectService defects, [FromForm] DefectStatus status) =>
        {
            await defects.SetStatusAsync(defectId, status);
            return Back(projectId, defectId);
        });

        qa.MapPost("/assignee", async (Guid projectId, Guid defectId, DefectService defects, [FromForm] string? assigneeId) =>
        {
            await defects.SetAssigneeAsync(defectId, string.IsNullOrWhiteSpace(assigneeId) ? null : assigneeId);
            return Back(projectId, defectId);
        });

        qa.MapPost("/test-cases/link", async (Guid projectId, Guid defectId, DefectService defects, [FromForm] Guid testCaseId) =>
        {
            await defects.LinkTestCaseAsync(defectId, testCaseId);
            return Back(projectId, defectId);
        });

        qa.MapPost("/test-cases/unlink", async (Guid projectId, Guid defectId, DefectService defects, [FromForm] Guid testCaseId) =>
        {
            await defects.UnlinkTestCaseAsync(defectId, testCaseId);
            return Back(projectId, defectId);
        });

        qa.MapPost("/test-cases/create", CreateLinkedTestCaseAsync);

        // Any authenticated user (QA and assigned Devs both comment on defects).
        var any = endpoints.MapGroup(BasePath).RequireAuthorization();

        any.MapPost("/comments", async (
            Guid projectId, Guid defectId, ClaimsPrincipal principal, DefectService defects, [FromForm] string body) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(body))
            {
                await defects.AddCommentAsync(defectId, userId, body);
            }
            return Back(projectId, defectId);
        });

        any.MapPost("/comments/{commentId:guid}/delete", async (
            Guid projectId, Guid defectId, Guid commentId, ClaimsPrincipal principal, DefectService defects) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var comment = (await defects.ListCommentsAsync(defectId)).FirstOrDefault(c => c.Id == commentId);
            if (comment is not null && (principal.IsInRole(Roles.QA) || comment.AuthorId == userId))
            {
                await defects.DeleteCommentAsync(commentId);
            }
            return Back(projectId, defectId);
        });

        return endpoints;
    }

    /// <summary>Fields of the "create a test case from this defect" form.</summary>
    public sealed record CreateTestCaseForm(string ScopeChoice, string? NewScopeName, TestCaseKind NewScopeKind);

    private static async Task<IResult> CreateLinkedTestCaseAsync(
        Guid projectId,
        Guid defectId,
        ClaimsPrincipal principal,
        DefectService defects,
        TestScopeService scopes,
        TestCaseService testCases,
        [FromForm] CreateTestCaseForm form)
    {
        var defect = await defects.GetAsync(defectId);
        if (defect is null || defect.ProjectId != projectId)
        {
            return Back(projectId, defectId);
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        Guid scopeId;
        if (form.ScopeChoice == "new")
        {
            if (string.IsNullOrWhiteSpace(form.NewScopeName))
            {
                return Back(projectId, defectId);
            }
            var scope = await scopes.CreateAsync(projectId, form.NewScopeKind, form.NewScopeName, userId);
            scopeId = scope.Id;
        }
        else if (!Guid.TryParse(form.ScopeChoice, out scopeId))
        {
            return Back(projectId, defectId);
        }

        var created = await testCases.CreateAsync(scopeId, new TestCaseInput(defect.Summary, defect.ReproSteps), userId);
        await defects.LinkTestCaseAsync(defectId, created.Id);
        return Back(projectId, defectId);
    }

    private static IResult Back(Guid projectId, Guid defectId) =>
        Results.LocalRedirect($"~/projects/{projectId}/defects/{defectId}");
}
