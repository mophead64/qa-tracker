using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaTracker.Web.Telemetry;

namespace QaTracker.UnitTests.Telemetry;

public class TelemetryOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData("Otlp", TelemetryProvider.Otlp)]
    [InlineData("otel", TelemetryProvider.Otlp)]
    [InlineData("OpenTelemetry", TelemetryProvider.Otlp)]
    [InlineData("AzureMonitor", TelemetryProvider.AzureMonitor)]
    [InlineData("applicationinsights", TelemetryProvider.AzureMonitor)]
    [InlineData("AppInsights", TelemetryProvider.AzureMonitor)]
    [InlineData("", TelemetryProvider.None)]
    [InlineData("nonsense", TelemetryProvider.None)]
    public void Resolves_provider_from_env_var(string value, TelemetryProvider expected)
    {
        var config = Config(new() { ["QATRACKER_TELEMETRY_PROVIDER"] = value });

        Assert.Equal(expected, TelemetryOptions.ResolveProvider(config));
    }

    [Fact]
    public void Provider_is_none_when_unset()
    {
        Assert.Equal(TelemetryProvider.None, TelemetryOptions.ResolveProvider(Config(new())));
    }

    [Fact]
    public void Service_name_defaults_to_qa_tracker()
    {
        Assert.Equal("qa-tracker", TelemetryOptions.ResolveServiceName(Config(new())));
    }

    [Fact]
    public void Service_name_is_taken_from_env_var()
    {
        var config = Config(new() { ["QATRACKER_TELEMETRY_SERVICE_NAME"] = "qa-tracker-staging" });

        Assert.Equal("qa-tracker-staging", TelemetryOptions.ResolveServiceName(config));
    }

    [Fact]
    public void Resolves_otlp_settings_with_grpc_default()
    {
        var config = Config(new()
        {
            ["QATRACKER_OTLP_ENDPOINT"] = "http://collector:4317",
            ["QATRACKER_TELEMETRY_SERVICE_NAME"] = "qa-tracker-eu",
        });

        var settings = TelemetryOptions.ResolveOtlp(config);

        Assert.Equal("http://collector:4317", settings.Endpoint);
        Assert.Equal("grpc", settings.Protocol);
        Assert.Equal("qa-tracker-eu", settings.ServiceName);
    }

    [Theory]
    [InlineData("http/protobuf", "http/protobuf")]
    [InlineData("http", "http/protobuf")]
    [InlineData("grpc", "grpc")]
    public void Normalises_otlp_protocol(string value, string expected)
    {
        var config = Config(new()
        {
            ["QATRACKER_OTLP_ENDPOINT"] = "http://collector:4318",
            ["QATRACKER_OTLP_PROTOCOL"] = value,
        });

        Assert.Equal(expected, TelemetryOptions.ResolveOtlp(config).Protocol);
    }

    [Fact]
    public void Rejects_unknown_otlp_protocol()
    {
        var config = Config(new()
        {
            ["QATRACKER_OTLP_ENDPOINT"] = "http://collector:4317",
            ["QATRACKER_OTLP_PROTOCOL"] = "carrier-pigeon",
        });

        Assert.Throws<InvalidOperationException>(() => TelemetryOptions.ResolveOtlp(config));
    }

    [Fact]
    public void Otlp_endpoint_is_required()
    {
        Assert.Throws<InvalidOperationException>(() => TelemetryOptions.ResolveOtlp(Config(new())));
    }

    [Fact]
    public void Resolves_azure_monitor_connection_string()
    {
        var config = Config(new()
        {
            ["QATRACKER_AZURE_MONITOR_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
        });

        var settings = TelemetryOptions.ResolveAzureMonitor(config);

        Assert.Equal("InstrumentationKey=00000000-0000-0000-0000-000000000000", settings.ConnectionString);
        Assert.Equal("qa-tracker", settings.ServiceName);
    }

    [Fact]
    public void Azure_monitor_connection_string_is_required()
    {
        Assert.Throws<InvalidOperationException>(() => TelemetryOptions.ResolveAzureMonitor(Config(new())));
    }

    [Fact]
    public void Export_log_level_defaults_to_warning_and_parses_overrides()
    {
        Assert.Equal(LogLevel.Warning, TelemetryOptions.ResolveExportLogLevel(Config(new())));
        Assert.Equal(LogLevel.Warning, TelemetryOptions.ResolveExportLogLevel(Config(new() { ["QATRACKER_TELEMETRY_LOG_LEVEL"] = "nonsense" })));
        Assert.Equal(LogLevel.Information, TelemetryOptions.ResolveExportLogLevel(Config(new() { ["QATRACKER_TELEMETRY_LOG_LEVEL"] = "information" })));
        Assert.Equal(LogLevel.Error, TelemetryOptions.ResolveExportLogLevel(Config(new() { ["QATRACKER_TELEMETRY_LOG_LEVEL"] = "Error" })));
    }
}
