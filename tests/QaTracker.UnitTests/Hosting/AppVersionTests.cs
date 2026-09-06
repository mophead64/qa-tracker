using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using QaTracker.Web.Hosting;

namespace QaTracker.UnitTests.Hosting;

public sealed class AppVersionTests
{
    [Fact]
    public void Development_environment_reports_Development()
    {
        Assert.Equal("Development", AppVersion.Describe(Env("Development")));
    }

    [Fact]
    public void Other_environments_report_a_clean_version_string()
    {
        var version = AppVersion.Describe(Env("Production"));

        Assert.NotEqual("Development", version);
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain('+', version); // the SDK's "+<source-revision>" suffix is trimmed
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
