using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.Storage;
using QaTracker.Web.Tags;
using QaTracker.Web.TestCases;

namespace DocsScreenshots;

/// <summary>
/// Fills an empty QA Tracker database with a fictional consultancy's year of QA work: eight
/// completed projects, two in testing and two coming up. Everything goes through the app's own
/// services, with <see cref="DemoClock"/> moved to each moment, so history timelines,
/// notifications, comments and reactions look exactly as they would in real use. Every person,
/// client and URL here is made up (example.com / .example).
/// </summary>
internal sealed partial class DemoSeeder
{
    private readonly DemoClock clock;
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly CommentReactionService reactions;
    private readonly AttachmentService attachments;
    private readonly TagService tags;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly IDbContextFactory<ApplicationDbContext> dbFactory;
    private readonly string samples;
    private readonly Dictionary<string, ApplicationUser> people = new();

    private DemoSeeder(IServiceProvider sp, DemoClock clock, string samples)
    {
        this.clock = clock;
        this.samples = samples;
        projects = sp.GetRequiredService<ProjectService>();
        scopes = sp.GetRequiredService<TestScopeService>();
        cases = sp.GetRequiredService<TestCaseService>();
        defects = sp.GetRequiredService<DefectService>();
        reactions = sp.GetRequiredService<CommentReactionService>();
        attachments = sp.GetRequiredService<AttachmentService>();
        tags = sp.GetRequiredService<TagService>();
        userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        dbFactory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    }

    public static async Task RunAsync(DocsOptions options)
    {
        var clock = new DemoClock();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["QATRACKER_STORAGE_PROVIDER"] = "S3",
            ["QATRACKER_S3_BUCKET"] = "qatracker-attachments",
            ["QATRACKER_S3_REGION"] = "us-east-1",
            ["QATRACKER_S3_ENDPOINT"] = options.S3Endpoint,
            ["QATRACKER_S3_ACCESS_KEY"] = "qatracker-docs",
            ["QATRACKER_S3_SECRET_KEY"] = "qatracker-docs-secret-key",
            ["QATRACKER_S3_FORCE_PATH_STYLE"] = "true",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseNpgsql(options.ConnectionString));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
        services.AddSingleton<TimeProvider>(clock);
        services.AddFileStorage(config);
        services.AddScoped<AttachmentService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<TestScopeService>();
        services.AddScoped<TestCaseService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<CommentReactionService>();
        services.AddScoped<DefectService>();
        services.AddScoped<TagService>();
        services.AddIdentityCore<ApplicationUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync())
        {
            if (await db.Projects.AnyAsync() || await db.Users.AnyAsync())
            {
                throw new InvalidOperationException(
                    "The database already has data. Seed a fresh stack: docker compose -f tools/docs-screenshots/docker-compose.yml down -v, then up -d.");
            }
        }

        var seeder = new DemoSeeder(scope.ServiceProvider, clock, options.SamplesDir);
        await seeder.SeedAsync();
    }

    private async Task SeedAsync()
    {
        Console.WriteLine("Seeding people…");
        await SeedPeopleAsync();
        await SeedTagsAsync();

        foreach (var history in HistoricalProjects)
        {
            Console.WriteLine($"Seeding {history.Name}…");
            await SeedHistoricalAsync(history);
        }

        Console.WriteLine("Seeding Meadowbrook Click & Collect…");
        await SeedMeadowbrookAsync();
        Console.WriteLine("Seeding upcoming projects…");
        await SeedUpcomingAsync();
        Console.WriteLine("Seeding Harbourline Checkout v2…");
        var harbourline = await SeedHarbourlineAsync();

        await FinishAsync(harbourline);
        Console.WriteLine("Done.");
    }

    // ---- people ---------------------------------------------------------------------------

    internal sealed record Person(string Key, string Name, string Role, bool Sso, string? LastLogin)
    {
        public string Email => Name.ToLowerInvariant().Replace(' ', '.') + "@example.com";
    }

    // Local accounts (Sso: false) sign in with DocsOptions.DemoPassword; the rest look like
    // accounts provisioned by Microsoft Entra ID on first sign-in.
    internal static readonly Person[] Cast =
    [
        new("priya", "Priya Raman", Roles.QA, Sso: false, "2026-11-10 17:28"),
        new("tom", "Tom Okafor", Roles.QA, Sso: true, "2026-11-10 17:02"),
        new("elena", "Elena Petrova", Roles.QA, Sso: true, "2026-11-10 15:12"),
        new("marcus", "Marcus Chen", Roles.Dev, Sso: false, "2026-11-10 16:41"),
        new("aisha", "Aisha Bello", Roles.Dev, Sso: true, "2026-11-10 17:39"),
        new("liam", "Liam Doyle", Roles.Dev, Sso: true, "2026-11-10 17:33"),
        new("hana", "Hana Sato", Roles.Dev, Sso: true, "2026-11-10 11:20"),
        new("diego", "Diego Alvarez", Roles.Dev, Sso: false, "2026-11-09 16:05"),
        new("noah", "Noah Fischer", Roles.Dev, Sso: true, "2026-11-06 10:44"),
        new("grace", "Grace Mensah", Roles.Dev, Sso: false, null),
        // The client's product owner: follows along read-only (never on a team).
        new("olivia", "Olivia Grant", Roles.Viewer, Sso: true, "2026-11-10 09:12"),
    ];

    private async Task SeedPeopleAsync()
    {
        clock.At("2026-02-02 09:00");
        foreach (var p in Cast)
        {
            var email = p.Email;
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = p.Name,
                Theme = ThemePreference.Dark,
                ExternalProvider = p.Sso ? "oidc" : null,
                LastLoginUtc = p.LastLogin is null ? null : DemoClock.Parse(p.LastLogin),
            };

            var result = p.Sso ? await userManager.CreateAsync(user) : await userManager.CreateAsync(user, DocsOptions.DemoPassword);
            Ensure(result);
            Ensure(await userManager.AddToRoleAsync(user, p.Role));
            if (p.Sso)
            {
                Ensure(await userManager.AddLoginAsync(user, new UserLoginInfo("oidc", Guid.NewGuid().ToString(), "Microsoft Entra ID")));
            }

            people[p.Key] = user;
        }
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private string Id(string key) => people[key].Id;

    private IReadOnlyList<string> Ids(params string[] keys) => keys.Select(Id).ToList();

    private bool IsQa(string key) => Cast.First(p => p.Key == key).Role == Roles.QA;

    // ---- small helpers over the services --------------------------------------------------

    private async Task<Guid> CreateProjectAsync(
        string when, string creator, string name, string brief, string? notes,
        IReadOnlyList<ProjectLinkInput> links, string[] team, ProjectSchedule schedule)
    {
        clock.At(when);
        var project = await projects.CreateAsync(name, brief, notes, links, Id(creator));
        clock.Tick(3);
        await projects.AddMembersAsync(project.Id, Ids(team), Id(creator));
        clock.Tick(4);
        await projects.UpdateScheduleAsync(project.Id, schedule);
        foreach (var tag in ProjectTags.GetValueOrDefault(name, []))
        {
            clock.Tick(1);
            await tags.AddToProjectAsync(project.Id, tag, Id(creator));
        }
        return project.Id;
    }

    // ---- tags -----------------------------------------------------------------------------

    private static readonly (string Name, TagColour Colour)[] TagCatalog =
    [
        ("Web", TagColour.Blue), ("Mobile", TagColour.Purple), ("API", TagColour.Green),
        ("Payments", TagColour.Amber), ("Retail", TagColour.Orange), ("Finance", TagColour.Red),
        ("Team Atlas", TagColour.Gray), ("Team Beacon", TagColour.Gray),
    ];

    /// <summary>Tags put on each project just after it's created (looked up by name).</summary>
    private static readonly Dictionary<string, string[]> ProjectTags = new()
    {
        ["Harbourline Checkout v2"] = ["Web", "Payments", "Retail", "Team Atlas"],
        ["Meadowbrook Click & Collect"] = ["Mobile", "Retail", "Team Beacon"],
        ["Wrenfield Quote Engine"] = ["API", "Finance", "Team Atlas"],
        ["Ashcombe Smart Meter Portal"] = ["Web", "Team Beacon"],
        ["Fernhill Appointment Booking"] = ["Web", "Team Beacon"],
        ["Quayside Shipment Tracking API"] = ["API", "Team Atlas"],
        ["Pinecrest Points Wallet"] = ["Mobile", "Retail"],
        ["Kestrel Offline Sync"] = ["Mobile"],
        ["Saltmarsh 3-D Secure Upgrade"] = ["Payments", "Finance"],
    };

    /// <summary>The shared tags, with their colours, before any project picks them up.</summary>
    private async Task SeedTagsAsync()
    {
        clock.At("2026-01-05 09:00");
        foreach (var (name, colour) in TagCatalog)
        {
            await tags.CreateAsync(name, colour);
        }
    }

    private async Task<Guid> ScopeAsync(Guid projectId, TestCaseKind kind, string name, string who)
    {
        clock.Tick(6);
        return (await scopes.CreateAsync(projectId, kind, name, Id(who))).Id;
    }

    private async Task<Guid> CaseAsync(Guid scopeId, string scenario, string? steps, string who)
    {
        clock.Tick(4);
        return (await cases.CreateAsync(scopeId, new TestCaseInput(scenario, steps), Id(who))).Id;
    }

    private async Task ResultAsync(string when, Guid testCaseId, TestResult result, string who)
    {
        clock.At(when);
        await cases.SetResultAsync(testCaseId, result, Id(who));
    }

    private async Task<Guid> DefectAsync(
        string when, Guid projectId, string who, string summary, DefectSeverity severity,
        string? repro, string? expected = null, string? actual = null, string? assignee = null,
        DefectTemplate template = DefectTemplate.Advanced, params Guid[] linkedCases)
    {
        clock.At(when);
        var defect = await defects.CreateAsync(projectId,
            new DefectInput(summary, repro, expected, actual, assignee is null ? null : Id(assignee), template),
            Id(who), severity);
        foreach (var testCaseId in linkedCases)
        {
            clock.Tick(1);
            await defects.LinkTestCaseAsync(defect.Id, testCaseId, Id(who));
        }

        return defect.Id;
    }

    /// <summary>"Mark as fixed" hands the defect to a random QA on the team; take the other QAs
    /// off the team for that moment so it deterministically lands with <paramref name="qa"/>.</summary>
    private async Task MarkFixedAsync(string when, Guid projectId, Guid defectId, string dev, string qa)
    {
        clock.At(when);
        var team = (await projects.GetAsync(projectId))!.Members.Select(m => m.Id).ToList();
        var otherQas = Cast.Where(p => p.Role == Roles.QA && p.Key != qa && team.Contains(Id(p.Key))).Select(p => Id(p.Key)).ToList();
        foreach (var other in otherQas)
        {
            await projects.RemoveMemberAsync(projectId, other);
        }

        await defects.MarkFixedAsync(defectId, Id(dev));
        await projects.AddMembersAsync(projectId, otherQas);

        // That team shuffle is a seeding trick, not something anyone did: keep it out of the
        // project's activity.
        await using var db = await dbFactory.CreateDbContextAsync();
        var at = clock.Now;
        await db.ProjectEvents
            .Where(e => e.ProjectId == projectId && e.OccurredUtc == at
                && (e.Kind == ProjectEventKind.MemberAdded || e.Kind == ProjectEventKind.MemberRemoved))
            .ExecuteDeleteAsync();
    }

    private async Task StartFixingAsync(string when, Guid defectId, string dev)
    {
        clock.At(when);
        await defects.StartFixingAsync(defectId, Id(dev));
    }

    private async Task VerifyAsync(string when, Guid defectId, string qa)
    {
        clock.At(when);
        await defects.VerifyFixedAsync(defectId, Id(qa));
    }

    private async Task RejectAsync(string when, Guid defectId, string qa)
    {
        clock.At(when);
        await defects.RejectFixAsync(defectId, Id(qa));
    }

    private async Task<Guid> DefectCommentAsync(string when, Guid defectId, string who, string body, params string[] mentions)
    {
        clock.At(when);
        return await defects.AddCommentAsync(defectId, Id(who), body, Ids(mentions));
    }

    private async Task<Guid> CaseCommentAsync(string when, Guid testCaseId, string who, string body, params string[] mentions)
    {
        clock.At(when);
        return await cases.AddCommentAsync(testCaseId, Id(who), body, Ids(mentions));
    }

    private async Task<Guid> ProjectCommentAsync(string when, Guid projectId, string who, string body, params string[] mentions)
    {
        clock.At(when);
        return await projects.AddCommentAsync(projectId, Id(who), body, Ids(mentions));
    }

    private async Task ReactAsync(string when, CommentKind kind, Guid commentId, string who, Reaction reaction = Reaction.Up)
    {
        clock.At(when);
        await reactions.ReactAsync(kind, commentId, Id(who), reaction);
    }

    private async Task AttachAsync(string when, AttachmentOwner owner, Guid ownerId, string file, string who, string? description = null)
    {
        clock.At(when);
        var path = Path.Combine(samples, file);
        await using var stream = File.OpenRead(path);
        await attachments.UploadAsync(owner, ownerId, stream, file, ContentType(file), new FileInfo(path).Length, description, Id(who));
    }

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".pdf" => "application/pdf",
        ".webm" => "video/webm",
        ".csv" => "text/csv",
        ".log" => "text/plain",
        ".har" => "application/json",
        _ => "application/octet-stream",
    };

    // ---- wrap-up ----------------------------------------------------------------------------

    /// <summary>Per-person preferences and read state, as if everyone had been using the app.</summary>
    private async Task FinishAsync(Guid harbourline)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        foreach (var user in await db.Users.ToListAsync())
        {
            user.CurrentProjectId = harbourline;
        }

        var priya = await db.Users.FirstAsync(u => u.Id == Id("priya"));
        priya.ProjectListFilter = ProjectListFilter.All;
        await db.SaveChangesAsync();

        // Everyone has opened the bell before now; leave what arrived since late morning unread.
        var cutoff = DemoClock.Parse(Hl.UnreadCutoff);
        await db.Notifications
            .Where(n => n.CreatedUtc < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadUtc, n => n.CreatedUtc.AddHours(1)));
    }
}
