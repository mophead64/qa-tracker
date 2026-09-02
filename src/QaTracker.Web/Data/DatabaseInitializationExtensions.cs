using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace QaTracker.Web.Data;

/// <summary>
/// Applies EF Core migrations and seeds baseline data. Runs synchronously during
/// startup (before the host starts) so the schema exists before Data Protection,
/// hosted services or any request touches the database. The app is never deployed
/// through CI/CD, so it is responsible for bringing its own schema up to date.
/// </summary>
public static class DatabaseInitializationExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<DatabaseInitializerMarker>>();

        var db = sp.GetRequiredService<ApplicationDbContext>();
        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync();

        await SeedRolesAsync(sp, logger);
        await SeedAdminUserAsync(sp, app.Configuration, logger);
    }

    private static async Task SeedRolesAsync(IServiceProvider sp, ILogger logger)
    {
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                logger.LogInformation("Creating role {Role}", role);
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task SeedAdminUserAsync(IServiceProvider sp, IConfiguration configuration, ILogger logger)
    {
        var email = configuration["QATRACKER_ADMIN_EMAIL"];
        var password = configuration["QATRACKER_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        logger.LogInformation("Creating bootstrap admin user {Email}", email);
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = "QA Tracker Admin",
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, Roles.QA);
        }
        else
        {
            logger.LogError("Failed to create admin user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>Category anchor for startup database logging.</summary>
    public sealed class DatabaseInitializerMarker;
}
