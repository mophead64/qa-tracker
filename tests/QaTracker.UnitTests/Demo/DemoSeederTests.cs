using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Demo;
using QaTracker.Web.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace QaTracker.UnitTests.Demo;

/// <summary>
/// The whole demo story, seeded through the real services into SQLite: it still runs end to end
/// after service changes (the docs screenshots and QATRACKER_DEMO_DATA both depend on it), it puts
/// in what the docs promise, and the clock offset moves every date.
/// </summary>
public sealed class DemoSeederTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");

    private DbContextOptions<ApplicationDbContext> Options =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(Options);
        await db.Database.EnsureCreatedAsync();
        foreach (var role in Roles.All)
        {
            db.Roles.Add(new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() });
        }
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    private Task SeedAsync(DemoClock clock) =>
        // No storage configured: the story goes in without its attachments.
        DemoSeeder.RunAsync(o => o.UseSqlite(connection), new ConfigurationBuilder().Build(), clock, NullLoggerFactory.Instance);

    [Fact]
    public async Task The_story_seeds_end_to_end_with_what_the_docs_describe()
    {
        await SeedAsync(new DemoClock());

        await using var db = new ApplicationDbContext(Options);
        Assert.Equal(12, await db.Projects.CountAsync());
        Assert.Equal(DemoSeeder.Cast.Length, await db.Users.CountAsync());
        Assert.Equal(121, await db.TestCases.CountAsync());
        Assert.Equal(47, await db.Defects.CountAsync());
        Assert.Equal(0, await db.Attachments.CountAsync());
        Assert.True(await db.ProjectComments.AnyAsync());
        // Harbourline's board: five stories, three bugs from defects, tasks, links and comments.
        Assert.Equal(1, await db.Projects.CountAsync(p => p.BoardMode != QaTracker.Web.Board.BoardMode.Off));
        Assert.Equal(5, await db.BoardItems.CountAsync(i => i.Type == QaTracker.Web.Board.BoardItemType.Story));
        Assert.Equal(4, await db.BoardItems.CountAsync(i => i.Type == QaTracker.Web.Board.BoardItemType.Bug)); // one known going in, three from defects
        Assert.Equal(17, await db.BoardItems.CountAsync(i => i.Type == QaTracker.Web.Board.BoardItemType.Task));
        Assert.Equal(3, await db.BoardItemLinks.CountAsync(l => l.DefectId != null && l.BoardItem!.Type == QaTracker.Web.Board.BoardItemType.Bug));
        Assert.True(await db.BoardItemComments.AnyAsync());
        Assert.True(await db.CommentReactions.AnyAsync());
        Assert.True(await db.Notifications.AnyAsync(n => n.ReadUtc == null), "some notifications are left unread");

        // Every state shows up somewhere.
        var statuses = await db.Projects.Select(p => p.Status).Distinct().ToListAsync();
        Assert.Contains(ProjectStatus.Complete, statuses);
        Assert.Contains(ProjectStatus.InFlight, statuses);
        Assert.Contains(ProjectStatus.Upcoming, statuses);
        var defectStatuses = await db.Defects.Select(d => d.Status).Distinct().ToListAsync();
        Assert.Equal(Enum.GetValues<DefectStatus>().Order(), defectStatuses.Order());

        // Everyone lands on the showcase project.
        var showcase = await db.Projects.SingleAsync(p => p.Name == DemoSeeder.ShowcaseProject);
        Assert.All(await db.Users.ToListAsync(), u => Assert.Equal(showcase.Id, u.CurrentProjectId));

        // The local demo accounts can sign in with the documented password.
        var priya = await db.Users.SingleAsync(u => u.Email == "priya.raman@example.com");
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<ApplicationUser>().VerifyHashedPassword(priya, priya.PasswordHash!, DemoSeeder.DemoPassword));
    }

    [Fact]
    public async Task An_offset_moves_every_date_in_the_story()
    {
        await SeedAsync(new DemoClock(TimeSpan.FromDays(14)));

        await using var db = new ApplicationDbContext(Options);
        var showcase = await db.Projects.SingleAsync(p => p.Name == DemoSeeder.ShowcaseProject);
        Assert.Equal(new DateOnly(2026, 11, 23), showcase.TestingStartDate);
        Assert.Equal(new DateOnly(2026, 11, 27), showcase.TestingEndDate);

        // Nothing is left behind at the story's own dates, or put after its shifted end.
        var shiftedEnd = DemoClock.StoryNow.AddDays(14);
        var created = (await db.ProjectEvents.Select(e => e.OccurredUtc).ToListAsync())
            .Concat(await db.DefectEvents.Select(e => e.OccurredUtc).ToListAsync())
            .Concat(await db.TestCaseEvents.Select(e => e.OccurredUtc).ToListAsync())
            .ToList();
        Assert.All(created, t => Assert.True(t <= shiftedEnd, $"{t:O} is after the story's end"));
        Assert.Contains(created, t => t > DemoClock.StoryNow);
    }
}

/// <summary>When start-up seeding (QATRACKER_DEMO_DATA) leaves the database alone.</summary>
public sealed class DemoDataTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private ServiceProvider services = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<ApplicationDbContext>(o => o.UseSqlite(connection))
            .BuildServiceProvider();
        await using var db = await services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static IConfiguration Config(string? value) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [DemoData.Setting] = value }).Build();

    private async Task<int> ProjectCountAsync()
    {
        await using var db = await services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();
        return await db.Projects.CountAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("nonsense")]
    public void It_is_off_unless_set_to_true(string? value) =>
        Assert.False(DemoData.Enabled(Config(value)));

    [Fact]
    public async Task Off_leaves_an_empty_database_empty()
    {
        // A bad connection string proves the seeder (which uses it) is never reached.
        await DemoData.SeedIfRequestedAsync(services, Config("false"), "Host=unreachable", NullLogger.Instance);

        Assert.Equal(0, await ProjectCountAsync());
    }

    [Fact]
    public async Task A_database_already_in_use_is_never_seeded()
    {
        await using (var db = await services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync())
        {
            db.Users.Add(new ApplicationUser { Id = "u", UserName = "u@test.local" });
            db.Projects.Add(new Project { Name = "Real work", CreatedById = "u" });
            await db.SaveChangesAsync();
        }

        await DemoData.SeedIfRequestedAsync(services, Config("true"), "Host=unreachable", NullLogger.Instance);

        Assert.Equal(1, await ProjectCountAsync());
    }

    [Fact]
    public async Task A_taken_demo_email_stops_it()
    {
        await using (var db = await services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync())
        {
            var email = DemoSeeder.Cast[0].Email;
            db.Users.Add(new ApplicationUser { Id = "u", UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant() });
            await db.SaveChangesAsync();
        }

        await DemoData.SeedIfRequestedAsync(services, Config("true"), "Host=unreachable", NullLogger.Instance);

        Assert.Equal(0, await ProjectCountAsync());
    }
}
