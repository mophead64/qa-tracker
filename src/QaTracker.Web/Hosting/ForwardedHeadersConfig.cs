using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace QaTracker.Web.Hosting;

/// <summary>
/// Resolves reverse-proxy header handling. Enabled by <c>QATRACKER_FORWARDED_HEADERS=true</c>
/// (default off, so a plain <c>dotnet run</c> can't be fooled by spoofed headers). When on,
/// <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c> are applied so the OIDC handler builds
/// <c>https://</c> callback URLs and request logging shows the real client.
///
/// <c>KnownProxies</c> / <c>KnownNetworks</c> are cleared by default — on Azure App Service and
/// Container Apps the only ingress path is the platform's own proxy. Override with
/// <c>QATRACKER_KNOWN_PROXIES</c> (comma-separated IPs) / <c>QATRACKER_KNOWN_NETWORKS</c>
/// (comma-separated CIDRs) when fronting the app with your own proxy.
/// </summary>
public static class ForwardedHeadersConfig
{
    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue("QATRACKER_FORWARDED_HEADERS", false);

    public static void Apply(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = configuration.GetValue<int?>("QATRACKER_FORWARDED_HEADERS_LIMIT");

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in Split(configuration["QATRACKER_KNOWN_PROXIES"]))
        {
            if (IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }

        foreach (var network in Split(configuration["QATRACKER_KNOWN_NETWORKS"]))
        {
            if (IPNetwork.TryParse(network, out var parsed))
            {
                options.KnownIPNetworks.Add(parsed);
            }
        }
    }

    private static string[] Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
