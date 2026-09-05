using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using QaTracker.Web.Hosting;
using IPNetwork = System.Net.IPNetwork;

namespace QaTracker.UnitTests.Hosting;

public class ForwardedHeadersConfigTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void IsEnabled_defaults_to_false()
    {
        Assert.False(ForwardedHeadersConfig.IsEnabled(Config(new())));
    }

    [Fact]
    public void IsEnabled_true_when_flag_set()
    {
        Assert.True(ForwardedHeadersConfig.IsEnabled(Config(new() { ["QATRACKER_FORWARDED_HEADERS"] = "true" })));
    }

    [Fact]
    public void Apply_sets_for_and_proto_and_clears_known_lists()
    {
        var options = new ForwardedHeadersOptions();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Any, 0));

        ForwardedHeadersConfig.Apply(options, Config(new()));

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Null(options.ForwardLimit);
    }

    [Fact]
    public void Apply_parses_known_proxies_and_networks_and_limit()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersConfig.Apply(options, Config(new()
        {
            ["QATRACKER_KNOWN_PROXIES"] = "10.0.0.4, 10.0.0.5",
            ["QATRACKER_KNOWN_NETWORKS"] = "10.0.0.0/16",
            ["QATRACKER_FORWARDED_HEADERS_LIMIT"] = "2",
        }));

        Assert.Equal(2, options.KnownProxies.Count);
        Assert.Contains(IPAddress.Parse("10.0.0.4"), options.KnownProxies);
        var network = Assert.Single(options.KnownIPNetworks);
        Assert.Equal(16, network.PrefixLength);
        Assert.Equal(2, options.ForwardLimit);
    }

    [Fact]
    public void Apply_ignores_unparseable_entries()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersConfig.Apply(options, Config(new()
        {
            ["QATRACKER_KNOWN_PROXIES"] = "not-an-ip",
            ["QATRACKER_KNOWN_NETWORKS"] = "garbage",
        }));

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }
}
