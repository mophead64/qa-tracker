namespace QaTracker.Web.Telemetry;

/// <summary>Which telemetry backend is configured. The system exports to one or none, never both.</summary>
public enum TelemetryProvider
{
    None,

    /// <summary>Azure Monitor / Application Insights, via the Azure Monitor OpenTelemetry distro.</summary>
    AzureMonitor,

    /// <summary>Any OTLP-capable backend (SigNoz, Grafana/Tempo, an OpenTelemetry Collector, …).</summary>
    Otlp,
}

/// <summary>Resolved settings for an OTLP exporter.</summary>
/// <param name="Endpoint">Collector/backend base URL, e.g. <c>http://localhost:4317</c>.</param>
/// <param name="Protocol">Normalised to <c>grpc</c> or <c>http/protobuf</c>.</param>
/// <param name="ServiceName">Logical service name reported as the <c>service.name</c> resource attribute.</param>
public sealed record OtlpTelemetrySettings(string Endpoint, string Protocol, string ServiceName);

/// <summary>Resolved settings for the Azure Monitor exporter.</summary>
public sealed record AzureMonitorTelemetrySettings(string ConnectionString, string ServiceName);

/// <summary>
/// Resolves the telemetry backend from <c>QATRACKER_TELEMETRY_PROVIDER</c> plus its
/// provider-specific env vars, mirroring the precedence style of
/// <see cref="Storage.StorageOptions"/> and <see cref="Data.DatabaseOptions"/>.
/// </summary>
public static class TelemetryOptions
{
    public const string DefaultServiceName = "qa-tracker";

    public static TelemetryProvider ResolveProvider(IConfiguration configuration) =>
        configuration["QATRACKER_TELEMETRY_PROVIDER"]?.Trim().ToUpperInvariant() switch
        {
            "AZUREMONITOR" or "AZURE" or "APPLICATIONINSIGHTS" or "APPINSIGHTS" or "AI" => TelemetryProvider.AzureMonitor,
            "OTLP" or "OTEL" or "OPENTELEMETRY" => TelemetryProvider.Otlp,
            _ => TelemetryProvider.None,
        };

    /// <summary>Logical service name for the telemetry resource (<c>QATRACKER_TELEMETRY_SERVICE_NAME</c>).</summary>
    public static string ResolveServiceName(IConfiguration configuration) =>
        configuration["QATRACKER_TELEMETRY_SERVICE_NAME"] is { Length: > 0 } name ? name.Trim() : DefaultServiceName;

    /// <summary>
    /// Minimum level of <see cref="ILogger"/> records shipped to the telemetry backend
    /// (<c>QATRACKER_TELEMETRY_LOG_LEVEL</c>, default <c>Warning</c> so exceptions flow but
    /// per-request info chatter doesn't). Traces and metrics are unaffected.
    /// </summary>
    public static LogLevel ResolveExportLogLevel(IConfiguration configuration) =>
        Enum.TryParse<LogLevel>(configuration["QATRACKER_TELEMETRY_LOG_LEVEL"], ignoreCase: true, out var level)
            ? level
            : LogLevel.Warning;

    public static OtlpTelemetrySettings ResolveOtlp(IConfiguration configuration) =>
        new(
            Endpoint: Require(configuration, "QATRACKER_OTLP_ENDPOINT"),
            Protocol: NormaliseProtocol(configuration["QATRACKER_OTLP_PROTOCOL"]),
            ServiceName: ResolveServiceName(configuration));

    public static AzureMonitorTelemetrySettings ResolveAzureMonitor(IConfiguration configuration) =>
        new(
            ConnectionString: Require(configuration, "QATRACKER_AZURE_MONITOR_CONNECTION_STRING"),
            ServiceName: ResolveServiceName(configuration));

    private static string NormaliseProtocol(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "grpc" => "grpc",
            "http" or "http/protobuf" or "httpprotobuf" => "http/protobuf",
            var other => throw new InvalidOperationException(
                $"QATRACKER_OTLP_PROTOCOL must be 'grpc' or 'http/protobuf', got '{other}'."),
        };

    private static string Require(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required for the configured QATRACKER_TELEMETRY_PROVIDER.");
}
