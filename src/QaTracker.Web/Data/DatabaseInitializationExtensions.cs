using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace QaTracker.Web.Data;

/// <summary>
/// Applies EF Core migrations and seeds baseline data. Runs synchronously during
/// startup (before the host starts) so the schema exists before Data Protection,
/// hosted services or any request touches the database. The app is never deployed
/// through CI/CD, so it is responsible for bringing its own schema up to date.
///
/// With more than one instance the whole migrate + seed step runs under a PostgreSQL
/// advisory lock (see <see cref="StartupDatabaseLock"/>) so concurrent starts are safe.
/// Set <c>QATRACKER_MIGRATE_ON_STARTUP=false</c> to have a dedicated migration step
/// (run the same image with <c>--migrate-only</c>) own the schema instead; instances
/// then verify the schema is current and refuse to start if it is not.
/// </summary>
public static class DatabaseInitializationExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<DatabaseInitializerMarker>>();

        var db = sp.GetRequiredService<ApplicationDbContext>();

        if (!app.Configuration.GetValue("QATRACKER_MIGRATE_ON_STARTUP", true))
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                throw new InvalidOperationException(
                    $"QATRACKER_MIGRATE_ON_STARTUP=false but {pending.Count} migration(s) are pending " +
                    $"({string.Join(", ", pending)}). Run the migration step (--migrate-only) before starting the app.");
            }

            logger.LogInformation("QATRACKER_MIGRATE_ON_STARTUP=false; schema is current, skipping migrate/seed.");
            return;
        }

        var connectionString = db.Database.GetConnectionString()
            ?? DatabaseOptions.ResolveConnectionString(app.Configuration);

        await using var _ = await StartupDatabaseLock.AcquireAsync(connectionString, logger);

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
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            logger.LogInformation("Creating role {Role}", role);
            try
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded &&
                    !result.Errors.Any(e => e.Code == "DuplicateRoleName"))
                {
                    logger.LogError("Failed to create role {Role}: {Errors}", role,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                logger.LogInformation(ex, "Role {Role} was created concurrently by another instance.", role);
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

        try
        {
            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, Roles.QA);
            }
            else if (!result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
            {
                logger.LogError("Failed to create admin user: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            logger.LogInformation(ex, "Admin user {Email} was created concurrently by another instance.", email);
        }

        // Whether we created the user or lost the race, make sure it ends up in the QA role.
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null && !await userManager.IsInRoleAsync(existing, Roles.QA))
        {
            await userManager.AddToRoleAsync(existing, Roles.QA);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Category anchor for startup database logging.</summary>
    public sealed class DatabaseInitializerMarker;
}
