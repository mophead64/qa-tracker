using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace QaTracker.Web.Data;

// Application user. Profile data beyond what ASP.NET Core Identity provides is added here.
public class ApplicationUser : IdentityUser
{
    [PersonalData]
    [MaxLength(200)]
    public string? FullName { get; set; }

    /// <summary>Preferred colour scheme, applied on every page render.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;
}
