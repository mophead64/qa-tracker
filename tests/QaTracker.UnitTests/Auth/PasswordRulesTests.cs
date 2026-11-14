using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;

namespace QaTracker.UnitTests.Auth;

/// <summary>The password rules shown beside password fields: worded from Identity's options, and
/// checked the same way Identity's validator checks them (password-field.js mirrors this).</summary>
public sealed class PasswordRulesTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");

    public void Dispose() => connection.Dispose();

    [Fact]
    public void Default_identity_options_list_length_and_every_character_class()
    {
        var rules = PasswordRules.For(new PasswordOptions());

        Assert.Equal(
            [
                ("length", "At least 6 characters"),
                ("lower", "A lowercase letter (a–z)"),
                ("upper", "An uppercase letter (A–Z)"),
                ("digit", "A number (0–9)"),
                ("symbol", "A symbol, such as ! @ # or -"),
            ],
            rules.Select(r => (r.Key, r.Text)));
    }

    [Fact]
    public void Rules_follow_the_configured_options()
    {
        var rules = PasswordRules.For(new PasswordOptions
        {
            RequiredLength = 12,
            RequireDigit = false,
            RequireNonAlphanumeric = false,
            RequiredUniqueChars = 4,
        });

        Assert.Equal(
            ["At least 12 characters", "A lowercase letter (a–z)", "An uppercase letter (A–Z)", "At least 4 different characters"],
            rules.Select(r => r.Text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("abcdef")]
    [InlineData("ABCDEF1!")]
    [InlineData("abcdef1!")]
    [InlineData("Abcdefg!")]
    [InlineData("Abcdef12")]
    [InlineData("Abcde1!")]
    [InlineData("Ünïcödé1!")]
    [InlineData("Str0ng!Passw0rd")]
    [InlineData("aaaaA1!")]
    public async Task Unmet_rules_agree_with_identitys_validator(string password)
    {
        var options = new PasswordOptions { RequiredUniqueChars = 3 };

        var identityErrors = await IdentityErrorsAsync(options, password);
        var unmet = PasswordRules.Unmet(options, password);

        Assert.Equal(identityErrors.Count == 0, unmet.Count == 0);
        Assert.Equal(identityErrors.Count, unmet.Count);
    }

    [Fact]
    public void Generated_length_and_symbols_satisfy_the_defaults()
    {
        Assert.True(PasswordRules.GeneratedLength >= new PasswordOptions().RequiredLength);
        Assert.All(PasswordRules.Symbols, c => Assert.False(char.IsAsciiLetterOrDigit(c)));
    }

    /// <summary>What Identity's own PasswordValidator says about <paramref name="password"/>.</summary>
    private async Task<IReadOnlyList<IdentityError>> IdentityErrorsAsync(PasswordOptions password, string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>(o => o.Password = password)
            .AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var errors = new List<IdentityError>();
        foreach (var validator in manager.PasswordValidators)
        {
            errors.AddRange((await validator.ValidateAsync(manager, new ApplicationUser(), value)).Errors);
        }

        return errors;
    }
}
