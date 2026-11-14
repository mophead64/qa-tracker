using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Data;

// Application user. Profile data beyond what ASP.NET Core Identity provides is added here.
public class ApplicationUser : IdentityUser
{
    [PersonalData]
    [MaxLength(200)]
    public string? FullName { get; set; }

    /// <summary>Preferred colour scheme, applied on every page render.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Whether a sound plays when a new notification arrives via polling (never for
    /// just opening the bell). See <see cref="NotificationSounds"/>.</summary>
    public bool NotificationSoundEnabled { get; set; } = true;

    /// <summary>Which sound plays for new notifications — a key from
    /// <see cref="NotificationSounds"/>.</summary>
    [MaxLength(32)]
    public string NotificationSound { get; set; } = NotificationSounds.Default;

    /// <summary>How loud that sound plays, as a percentage of the device volume — one of
    /// <see cref="NotificationSounds.Volumes"/>.</summary>
    public int NotificationVolume { get; set; } = NotificationSounds.DefaultVolume;

    /// <summary>Which projects the All Projects page shows; remembered between logins.</summary>
    public ProjectListFilter ProjectListFilter { get; set; } = ProjectListFilter.ActiveAndUpcoming;

    /// <summary>Which test cases the test cases page shows; one choice for every project,
    /// remembered between logins.</summary>
    public TestCaseListFilter TestCaseListFilter { get; set; } = TestCaseListFilter.All;

    /// <summary>Whether the test cases page shows scopes expanded or collapsed; one choice for
    /// every project, remembered between logins.</summary>
    public TestScopeLayout TestScopeLayout { get; set; } = TestScopeLayout.Expanded;

    /// <summary>Which defects the defects page shows; one choice for every project,
    /// remembered between logins.</summary>
    public Defects.DefectListFilter DefectListFilter { get; set; } = Defects.DefectListFilter.All;

    /// <summary>
    /// Set when the account was deleted but still has history attached (projects, test cases,
    /// defects, comments or files it created, fixes or results it recorded). The row is kept so
    /// that history stays intact, renamed <see cref="DeletedName"/> with no sign-in, roles or teams,
    /// and left out of every user list and picker. Accounts with nothing attached are removed.
    /// </summary>
    public DateTimeOffset? DeletedUtc { get; set; }

    /// <summary>What a deleted account is shown as wherever its history appears.</summary>
    public const string DeletedName = "Deleted user";

    /// <summary>Notification triggers this user has switched on that are off by default — a bit per
    /// <see cref="Notifications.NotificationTrigger"/>. Only choices that differ from the default are
    /// stored (see <see cref="Notifications.NotificationTriggers"/>).</summary>
    public long NotificationsOn { get; set; }

    /// <summary>Notification triggers this user has switched off that are on by default.</summary>
    public long NotificationsOff { get; set; }

    /// <summary>
    /// The external identity provider that owns this account (e.g. "oidc"), or null for a
    /// local password account. When set, email / password / roles are controlled by the
    /// provider and cannot be changed in-app. The canonical login link lives in
    /// <c>AspNetUserLogins</c>; this is a denormalised flag for display and gating.
    /// </summary>
    [MaxLength(64)]
    public string? ExternalProvider { get; set; }

    /// <summary>
    /// The project the user last switched to. Drives the default dashboard and the
    /// project-scoped side navigation. Null until the user picks one.
    /// </summary>
    public Guid? CurrentProjectId { get; set; }

    /// <summary>When the user last completed a sign-in (local password or SSO). Null if
    /// they've never signed in since this was added.</summary>
    public DateTimeOffset? LastLoginUtc { get; set; }
}
