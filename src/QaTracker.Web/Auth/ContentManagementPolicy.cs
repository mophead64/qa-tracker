using Microsoft.AspNetCore.Authorization;
using QaTracker.Web.Admin;
using QaTracker.Web.Data;

namespace QaTracker.Web.Auth;

/// <summary>
/// Authorization policy names for creating / editing / deleting the core content types.
/// QA always satisfies them; a developer satisfies one only when the matching flag is
/// enabled in system settings (the default). Purely QA workflow — setting a test result,
/// changing a defect's status or assignee — stays gated on <see cref="Roles.QA"/> directly.
/// </summary>
public static class Policies
{
    public const string ManageProjects = "ManageProjects";
    public const string ManageTestCases = "ManageTestCases";
    public const string ManageDefects = "ManageDefects";

    public static string For(ManageableArea area) => area switch
    {
        ManageableArea.Projects => ManageProjects,
        ManageableArea.TestCases => ManageTestCases,
        ManageableArea.Defects => ManageDefects,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, null),
    };
}

public sealed class ManageAreaRequirement(ManageableArea area) : IAuthorizationRequirement
{
    public ManageableArea Area { get; } = area;
}

/// <summary>
/// Grants a manage-area requirement to any QA, and to a developer when system settings
/// currently allow developers to manage that area.
/// </summary>
public sealed class ManageAreaHandler(SystemSettingsService settings)
    : AuthorizationHandler<ManageAreaRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ManageAreaRequirement requirement)
    {
        if (context.User.IsInRole(Roles.QA))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.User.IsInRole(Roles.Dev)
            && await settings.DevelopersCanManageAsync(requirement.Area))
        {
            context.Succeed(requirement);
        }
    }
}
