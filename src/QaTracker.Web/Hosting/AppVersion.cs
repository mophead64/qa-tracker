using System.Reflection;

namespace QaTracker.Web.Hosting;

/// <summary>
/// The running build's version, shown in the "System status" panel on /admin (and, later,
/// an "update available" check against the GitHub repo). "Development" in the Development
/// environment; otherwise the assembly informational version — CI stamps the git tag into
/// it — with the SDK's trailing "+&lt;source-revision&gt;" suffix trimmed off.
/// </summary>
public static class AppVersion
{
    public static string Describe(IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return "Development";
        }

        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var version = informational?.Split('+')[0];
        return string.IsNullOrEmpty(version) ? "unknown" : version;
    }
}
