using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;

namespace QaTracker.Web.Demo;

/// <summary>
/// QATRACKER_DEMO_DATA=true fills a brand-new install with the demo story (<see cref="DemoSeeder"/>):
/// a dozen projects in every state, ten people, test runs, defects, comments, reactions, files and
/// notifications — shifted so it reads as this week. It only ever runs against an empty database
/// (no projects, no tags), so restarting, or turning it on for an install already in use, changes
/// nothing; set it to false (the default) for a blank system. Runs during start-up, under the same
/// advisory lock as migrations, so only one instance seeds.
/// </summary>
public static class DemoData
{
    public const string Setting = "QATRACKER_DEMO_DATA";

    public static bool Enabled(IConfiguration configuration) => configuration.GetValue(Setting, false);

    public static async Task SeedIfRequestedAsync(IServiceProvider sp, IConfiguration configuration, string connectionString, ILogger logger)
    {
        if (!Enabled(configuration))
        {
            return;
        }

        await using (var db = await sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync())
        {
            if (await db.Projects.AnyAsync() || await db.Tags.AnyAsync())
            {
                logger.LogInformation("{Setting}=true, but the database already has projects; leaving it as it is.", Setting);
                return;
            }

            var castEmails = DemoSeeder.Cast.Select(p => p.Email.ToUpperInvariant()).ToList();
            if (await db.Users.AnyAsync(u => castEmails.Contains(u.NormalizedEmail!)))
            {
                logger.LogWarning("{Setting}=true, but a demo person's email is already taken; not seeding.", Setting);
                return;
            }
        }

        var clock = DemoClock.AlignedTo(TimeProvider.System.GetUtcNow());
        logger.LogInformation("{Setting}=true and the database is empty: adding the demo projects…", Setting);
        try
        {
            await DemoSeeder.RunAsync(connectionString, configuration, clock, sp.GetRequiredService<ILoggerFactory>());
            logger.LogInformation(
                "Demo data added. Demo sign-ins (password {Password}): priya.raman@example.com (QA), marcus.chen@example.com (Dev).",
                DemoSeeder.DemoPassword);
        }
        catch (Exception ex)
        {
            // Don't stop the app over demo data; what got in before the failure stays.
            logger.LogError(ex, "Adding the demo data failed part-way; the app will start with what was added.");
        }
    }
}
