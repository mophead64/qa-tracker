using DocsScreenshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaTracker.Web.Data;
using QaTracker.Web.Demo;

// Usage (from the repo root; run.sh wraps the whole thing):
//   dotnet run --project tools/docs-screenshots -- seed            # demo data into an empty database
//   dotnet run --project tools/docs-screenshots -- samples         # re-render the demo attachments
//                                                                  #   (src/QaTracker.Web/Demo/Samples)
//   dotnet run --project tools/docs-screenshots -- shots [name…]   # capture all (or some) screenshots
//   dotnet run --project tools/docs-screenshots -- login-sso-only  # the SSO-only login page (app restarted
//                                                                  #   with QATRACKER_DOCS_LOCAL_AUTH=false)
var options = DocsOptions.FromEnvironment();
var command = args.FirstOrDefault() ?? "help";
var rest = args.Skip(1).ToArray();

switch (command)
{
    case "samples":
        await SampleFiles.GenerateAsync(options.SamplesDir);
        break;
    case "seed":
        await SeedAsync(options);
        break;
    case "shots":
        await Shots.RunAsync(options, rest);
        break;
    case "login-sso-only":
        await Shots.RunSsoOnlyLoginAsync(options);
        break;
    default:
        Console.WriteLine("commands: samples | seed | shots [name…] | login-sso-only");
        return 1;
}

return 0;

// The app's own demo seeder (src/QaTracker.Web/Demo), at the story's real dates (no offset) and in
// dark mode, so the screenshots never drift.
static async Task SeedAsync(DocsOptions options)
{
    await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(options.ConnectionString).Options))
    {
        if (await db.Projects.AnyAsync() || await db.Users.AnyAsync())
        {
            throw new InvalidOperationException(
                "The database already has data. Seed a fresh stack: docker compose -f tools/docs-screenshots/docker-compose.yml down -v, then up -d.");
        }
    }

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

    using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information)
        .AddFilter("Microsoft", LogLevel.Warning).AddFilter("System", LogLevel.Warning));
    await DemoSeeder.RunAsync(options.ConnectionString, config, new DemoClock(), loggers, ThemePreference.Dark);
}

namespace DocsScreenshots
{
    internal sealed record DocsOptions(
        string BaseUrl,
        string ConnectionString,
        string S3Endpoint,
        string RepoRoot,
        string OutputDir,
        string SamplesDir,
        string ScratchDir)
    {
        /// <summary>Every local demo account signs in with this password.</summary>
        public const string DemoPassword = DemoSeeder.DemoPassword;

        public static DocsOptions FromEnvironment()
        {
            var root = FindRepoRoot();
            return new DocsOptions(
                BaseUrl: (Environment.GetEnvironmentVariable("QATRACKER_DOCS_BASEURL") ?? "http://localhost:8090").TrimEnd('/'),
                ConnectionString: Environment.GetEnvironmentVariable("QATRACKER_DOCS_DB")
                    ?? "Host=localhost;Port=55432;Database=qatracker;Username=qatracker;Password=qatracker",
                S3Endpoint: Environment.GetEnvironmentVariable("QATRACKER_DOCS_S3") ?? "http://localhost:39000",
                RepoRoot: root,
                OutputDir: Environment.GetEnvironmentVariable("QATRACKER_DOCS_OUT") ?? Path.Combine(root, "docs", "screenshots"),
                // The demo attachments live in the app (embedded); "samples" re-renders them there.
                SamplesDir: Path.Combine(root, "src", "QaTracker.Web", "Demo", "Samples"),
                ScratchDir: Path.Combine(root, "tools", "docs-screenshots", "obj", "scratch"));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QaTracker.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Run from inside the qa-tracker repository.");
        }
    }
}
