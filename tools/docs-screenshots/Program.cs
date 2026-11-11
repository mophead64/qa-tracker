using DocsScreenshots;

// Usage (from the repo root; run.sh wraps the whole thing):
//   dotnet run --project tools/docs-screenshots -- seed            # demo data into an empty database
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
        await SampleFiles.GenerateAsync(options.SamplesDir);
        await DemoSeeder.RunAsync(options);
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

namespace DocsScreenshots
{
    internal sealed record DocsOptions(
        string BaseUrl,
        string ConnectionString,
        string S3Endpoint,
        string RepoRoot,
        string OutputDir,
        string SamplesDir)
    {
        /// <summary>Every local demo account signs in with this password.</summary>
        public const string DemoPassword = "Demo!Passw0rd";

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
                SamplesDir: Path.Combine(root, "tools", "docs-screenshots", "obj", "samples"));
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

    /// <summary>A clock the seeder moves around, so every record and history entry gets a
    /// believable timestamp. Times are written as UTC (London is on UTC in November).</summary>
    internal sealed class DemoClock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Set(DateTimeOffset utc) => Now = utc;

        /// <summary>"2026-11-09 14:20" (UTC).</summary>
        public void At(string utc) => Now = Parse(utc);

        public void Tick(int minutes = 1) => Now = Now.AddMinutes(minutes);

        public static DateTimeOffset Parse(string utc) =>
            // global:: because TimeProvider.System shadows the System namespace in here.
            new(DateTime.ParseExact(utc, "yyyy-MM-dd HH:mm", global::System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero);
    }
}
