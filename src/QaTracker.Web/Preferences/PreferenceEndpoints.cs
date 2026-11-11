using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Preferences;

/// <summary>
/// The signed-in user's saved list preferences — one choice each, across every project and
/// login: the All Projects filter, the test cases page filter and layout, and the defects page
/// filter. Each POST saves the choice and goes back to <c>returnUrl</c> (same-site only).
/// See <see cref="AppUrls"/>.
/// </summary>
public static class PreferenceEndpoints
{
    public static IEndpointRouteBuilder MapPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/preferences").RequireAuthorization();

        group.MapPost("/project-filter", (ClaimsPrincipal principal, [FromServices] UserManager<ApplicationUser> users,
                [FromForm] ProjectListFilter filter, [FromForm] string? returnUrl) =>
            SaveAsync(principal, users, filter, u => u.ProjectListFilter, (u, v) => u.ProjectListFilter = v, returnUrl ?? "/projects"));

        group.MapPost("/test-case-filter", (ClaimsPrincipal principal, [FromServices] UserManager<ApplicationUser> users,
                [FromForm] TestCaseListFilter filter, [FromForm] string? returnUrl) =>
            SaveAsync(principal, users, filter, u => u.TestCaseListFilter, (u, v) => u.TestCaseListFilter = v, returnUrl));

        group.MapPost("/test-case-layout", (ClaimsPrincipal principal, [FromServices] UserManager<ApplicationUser> users,
                [FromForm] TestScopeLayout layout, [FromForm] string? returnUrl) =>
            SaveAsync(principal, users, layout, u => u.TestScopeLayout, (u, v) => u.TestScopeLayout = v, returnUrl));

        group.MapPost("/defect-filter", (ClaimsPrincipal principal, [FromServices] UserManager<ApplicationUser> users,
                [FromForm] DefectListFilter filter, [FromForm] string? returnUrl) =>
            SaveAsync(principal, users, filter, u => u.DefectListFilter, (u, v) => u.DefectListFilter = v, returnUrl));

        return endpoints;
    }

    private static async Task<IResult> SaveAsync<T>(
        ClaimsPrincipal principal, UserManager<ApplicationUser> users, T value,
        Func<ApplicationUser, T> get, Action<ApplicationUser, T> set, string? returnUrl)
        where T : struct, Enum
    {
        var user = await users.GetUserAsync(principal);
        if (user is not null && Enum.IsDefined(value) && !EqualityComparer<T>.Default.Equals(get(user), value))
        {
            set(user, value);
            await users.UpdateAsync(user);
        }

        var back = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";
        return Results.LocalRedirect($"~{back}");
    }
}
