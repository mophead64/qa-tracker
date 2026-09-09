using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using QaTracker.Web.Hosting;

namespace QaTracker.UnitTests.Hosting;

public sealed class BuildInfoTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Development_with_no_stamp_reports_Development()
    {
        var info = BuildInfo.FromConfiguration(Config([]), Env("Development"));

        Assert.Equal("Development", info.Version);
        Assert.True(info.IsDevelopment);
        Assert.False(info.UpdateCheckEnabled);
        Assert.Null(info.SourceRef);
        Assert.Null(info.CommitUrl);
    }

    [Fact]
    public void Non_development_with_no_stamp_reports_unknown()
    {
        var info = BuildInfo.FromConfiguration(Config([]), Env("Production"));

        Assert.Equal("unknown", info.Version);
        Assert.False(info.IsDevelopment);
    }

    [Fact]
    public void Stamped_values_populate_the_record()
    {
        var info = BuildInfo.FromConfiguration(Config(new()
        {
            ["QATRACKER_BUILD_VERSION"] = "2026.09.09",
            ["QATRACKER_BUILD_BRANCH"] = "master",
            ["QATRACKER_BUILD_COMMIT"] = "abcdef1234567890",
            ["QATRACKER_BUILD_COMMIT_SHORT"] = "abcdef1",
            ["QATRACKER_BUILD_DATE"] = "2026-09-09T10:11:12Z",
        }), Env("Production"));

        Assert.Equal("2026.09.09", info.Version);
        Assert.Equal("master:abcdef1", info.SourceRef);
        Assert.Equal("https://github.com/mophead64/qa-tracker/commit/abcdef1234567890", info.CommitUrl);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 10, 11, 12, TimeSpan.Zero), info.BuildDate);
        Assert.True(info.UpdateCheckEnabled);
    }

    [Fact]
    public void Stamped_version_wins_even_in_Development()
    {
        var info = BuildInfo.FromConfiguration(
            Config(new() { ["QATRACKER_BUILD_VERSION"] = "2026.09.09" }), Env("Development"));

        Assert.Equal("2026.09.09", info.Version);
        Assert.True(info.UpdateCheckEnabled);
    }

    [Fact]
    public void GitHub_repo_defaults_and_can_be_overridden()
    {
        Assert.Equal("mophead64/qa-tracker",
            BuildInfo.FromConfiguration(Config([]), Env("Production")).GitHubRepo);

        var forked = BuildInfo.FromConfiguration(
            Config(new() { ["QATRACKER_GITHUB_REPO"] = "acme/qa-tracker" }), Env("Production"));
        Assert.Equal("acme/qa-tracker", forked.GitHubRepo);
        Assert.StartsWith("https://github.com/acme/qa-tracker/releases", forked.ReleasesUrl);
    }

    [Fact]
    public void Update_check_can_be_switched_off()
    {
        var info = BuildInfo.FromConfiguration(Config(new()
        {
            ["QATRACKER_BUILD_VERSION"] = "2026.09.09",
            ["QATRACKER_UPDATE_CHECK"] = "false",
        }), Env("Production"));

        Assert.False(info.UpdateCheckEnabled);
    }

    private static IHostEnvironment Env(string name) => new FakeHostEnvironment { EnvironmentName = name };

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "QaTracker.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
