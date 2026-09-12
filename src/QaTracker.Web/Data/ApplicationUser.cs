using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using QaTracker.Web.Notifications;

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
