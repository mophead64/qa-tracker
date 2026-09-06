using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Data;

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

        qa.MapPost("/severity", async (Guid projectId, Guid defectId, DefectService defects, [FromForm] DefectSeverity severity) =>
        {
            await defects.SetSeverityAsync(defectId, severity);
            return Back(projectId, defectId);
        });

        qa.MapPost("/assignee", async (Guid projectId, Guid defectId, DefectService defects, [FromForm] string? assigneeId) =>
        {
            await defects.SetAssigneeAsync(defectId, string.IsNullOrWhiteSpace(assigneeId) ? null : assigneeId);
            return Back(projectId, defectId);
        });

        // The "Link test cases" modal's form: link every checked existing case. Creating a
        // *new* case happens on the test-case pages instead (they carry a ?fromDefect= param
        // that prefills the case and links it back here on save).
        qa.MapPost("/test-cases/link", LinkTestCasesAsync);

        qa.MapPost("/test-cases/unlink", async (Guid projectId, Guid defectId, DefectService defects, [FromForm] Guid testCaseId) =>
        {
            await defects.UnlinkTestCaseAsync(defectId, testCaseId);
            return Back(projectId, defectId);
        });

        // Any authenticated user (QA and assigned Devs both comment on defects).
        var any = endpoints.MapGroup(BasePath).RequireAuthorization();

        // Self-service assignment: a Dev or QA can pick up a defect or drop one they hold.
        // Assigning to anyone else stays QA-only (the "/assignee" endpoint above).
        any.MapPost("/assignee/self", AssignSelfAsync);

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

    private static async Task<IResult> AssignSelfAsync(
        Guid projectId,
        Guid defectId,
        ClaimsPrincipal principal,
        DefectService defects,
        [FromForm] bool assign)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            if (assign)
            {
                await defects.SetAssigneeAsync(defectId, userId);
            }
            else
            {
                // Only clear it if the caller is actually the current assignee.
                var defect = await defects.GetAsync(defectId);
                if (defect?.AssignedToId == userId)
                {
                    await defects.SetAssigneeAsync(defectId, null);
                }
            }
        }

        return Back(projectId, defectId);
    }

    /// <summary>
    /// Backs the "Link test cases" modal — links every checked existing case (the
    /// <c>testCaseIds</c> checkbox list). <see cref="FromFormAttribute"/> can't bind the
    /// checkbox array, so the form is read directly; an <see cref="IFormCollection"/>
    /// parameter still enforces antiforgery.
    /// </summary>
    private static async Task<IResult> LinkTestCasesAsync(
        Guid projectId,
        Guid defectId,
        DefectService defects,
        IFormCollection form)
    {
        var defect = await defects.GetAsync(defectId);
        if (defect is null || defect.ProjectId != projectId)
        {
            return Back(projectId, defectId);
        }

        foreach (var raw in form["testCaseIds"])
        {
            if (Guid.TryParse(raw, out var testCaseId))
            {
                await defects.LinkTestCaseAsync(defectId, testCaseId);
            }
        }

        return Back(projectId, defectId);
    }

    private static IResult Back(Guid projectId, Guid defectId) =>
        Results.LocalRedirect($"~/projects/{projectId}/defects/{defectId}");
}
