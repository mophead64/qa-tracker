using Microsoft.Extensions.Configuration;
using QaTracker.Web.Data;

namespace QaTracker.UnitTests;

public class DatabaseOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Prefers_explicit_connection_string()
    {
        var config = Config(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=explicit;Username=u;Password=p",
            ["QATRACKER_DB_HOST"] = "ignored",
        });

        var result = DatabaseOptions.ResolveConnectionString(config);

        Assert.Equal("Host=db;Database=explicit;Username=u;Password=p", result);
    }

    [Fact]
    public void Assembles_connection_string_from_discrete_settings()
    {
        var config = Config(new()
        {
            ["QATRACKER_DB_HOST"] = "postgres",
            ["QATRACKER_DB_PORT"] = "6432",
            ["QATRACKER_DB_NAME"] = "qat",
            ["QATRACKER_DB_USER"] = "svc",
            ["QATRACKER_DB_PASSWORD"] = "secret",
        });

        var result = DatabaseOptions.ResolveConnectionString(config);

        Assert.Contains("Host=postgres", result);
        Assert.Contains("Port=6432", result);
        Assert.Contains("Database=qat", result);
        Assert.Contains("Username=svc", result);
        Assert.Contains("Password=secret", result);
    }

    [Fact]
    public void Falls_back_to_local_defaults()
    {
        var result = DatabaseOptions.ResolveConnectionString(Config(new()));

        Assert.Contains("Host=localhost", result);
        Assert.Contains("Database=qatracker", result);
    }

    [Fact]
    public void Assembled_string_carries_default_pool_bounds()
    {
        var result = DatabaseOptions.ResolveConnectionString(Config(new()));

        Assert.Contains("Maximum Pool Size=20", result);
        Assert.Contains("Minimum Pool Size=1", result);
    }

    [Fact]
    public void Pool_bounds_honour_overrides()
    {
        var config = Config(new()
        {
            ["QATRACKER_DB_MAX_POOL_SIZE"] = "50",
            ["QATRACKER_DB_MIN_POOL_SIZE"] = "5",
        });

        var result = DatabaseOptions.ResolveConnectionString(config);

        Assert.Contains("Maximum Pool Size=50", result);
        Assert.Contains("Minimum Pool Size=5", result);
    }

    [Fact]
    public void Explicit_connection_string_is_passed_through_untouched()
    {
        var config = Config(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=explicit;Username=u;Password=p",
            ["QATRACKER_DB_MAX_POOL_SIZE"] = "50",
        });

        var result = DatabaseOptions.ResolveConnectionString(config);

        Assert.Equal("Host=db;Database=explicit;Username=u;Password=p", result);
        Assert.DoesNotContain("Pool Size", result);
    }
}
