using Microsoft.AspNetCore.Identity;

namespace QaTracker.Web.Auth;

/// <summary>One requirement a new password must meet, as shown beside password fields.
/// <paramref name="Key"/> is what password-field.js checks it by.</summary>
public sealed record PasswordRule(string Key, string Text);

/// <summary>
/// The password requirements ASP.NET Core Identity enforces (<see cref="PasswordOptions"/>),
/// phrased for people. Password fields list these and password-field.js ticks them off as you
/// type, so a password that won't pass is caught before the form posts — the server still
/// validates, through Identity, either way.
/// </summary>
public static class PasswordRules
{
    /// <summary>Characters a generated password may use as its symbol (all accepted by Identity's
    /// non-alphanumeric rule, none that are awkward to read out or paste into a shell).</summary>
    public const string Symbols = "!@#$%^&*-_=+?";

    /// <summary>Length of a generated password — comfortably past any sensible minimum.</summary>
    public const int GeneratedLength = 20;

    public static IReadOnlyList<PasswordRule> For(PasswordOptions options)
    {
        var rules = new List<PasswordRule>
        {
            new("length", $"At least {options.RequiredLength} {(options.RequiredLength == 1 ? "character" : "characters")}"),
        };
        if (options.RequireLowercase) rules.Add(new("lower", "A lowercase letter (a–z)"));
        if (options.RequireUppercase) rules.Add(new("upper", "An uppercase letter (A–Z)"));
        if (options.RequireDigit) rules.Add(new("digit", "A number (0–9)"));
        if (options.RequireNonAlphanumeric) rules.Add(new("symbol", "A symbol, such as ! @ # or -"));
        if (options.RequiredUniqueChars > 1) rules.Add(new("unique", $"At least {options.RequiredUniqueChars} different characters"));
        return rules;
    }

    /// <summary>The rules <paramref name="password"/> doesn't meet (by <see cref="PasswordRule.Key"/>),
    /// checked the same way Identity's validator and password-field.js do.</summary>
    public static IReadOnlyList<string> Unmet(PasswordOptions options, string password)
    {
        var unmet = new List<string>();
        if (password.Length < options.RequiredLength) unmet.Add("length");
        if (options.RequireLowercase && !password.Any(char.IsAsciiLetterLower)) unmet.Add("lower");
        if (options.RequireUppercase && !password.Any(char.IsAsciiLetterUpper)) unmet.Add("upper");
        if (options.RequireDigit && !password.Any(char.IsAsciiDigit)) unmet.Add("digit");
        if (options.RequireNonAlphanumeric && password.All(char.IsAsciiLetterOrDigit)) unmet.Add("symbol");
        if (options.RequiredUniqueChars > 1 && password.Distinct().Count() < options.RequiredUniqueChars) unmet.Add("unique");
        return unmet;
    }
}
