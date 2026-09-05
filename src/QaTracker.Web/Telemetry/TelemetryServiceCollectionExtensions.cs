using System.Reflection;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QaTracker.Web.Hosting;

namespace QaTracker.Web.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    private static readonly string ServiceVersion =
        typeof(TelemetryServiceCollectionExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

    /// <summary>
    /// Wires up OpenTelemetry for the backend selected by <c>QATRACKER_TELEMETRY_PROVIDER</c>
    /// (Azure Monitor or OTLP): traces for ASP.NET Core, outbound HTTP and PostgreSQL — with
    /// exceptions recorded as span events — plus metrics and logs on the OTLP path. A no-op
    /// when the provider is unset. Returns a one-line summary for the startup log.
    /// </summary>
    public static string AddTelemetry(this WebApplicationBuilder builder)
    {
        var provider = TelemetryOptions.ResolveProvider(builder.Configuration);
        if (provider == TelemetryProvider.None)
        {
            return "disabled (QATRACKER_TELEMETRY_PROVIDER unset)";
        }

        var serviceName = TelemetryOptions.ResolveServiceName(builder.Configuration);

        // Surface the SDK's own export/config failures in the console instead of silently.
        // Keep this class's output out of the log exporter so a failing exporter can't
        // feed its own error back into itself.
        builder.Services.AddSingleton<OpenTelemetryDiagnostics>();
        builder.Logging.AddFilter<OpenTelemetry.Logs.OpenTelemetryLoggerProvider>(
            typeof(OpenTelemetryDiagnostics).FullName, LogLevel.None);

        // Record exceptions as span events (off by default) so failures show up under
        // Exceptions in the backend, and keep static assets and the Blazor framework /
        // circuit endpoints out of traces — regardless of which package registers the
        // ASP.NET Core listener (the Azure Monitor distro adds it internally, we add it
        // otherwise).
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            options.RecordException = true;
            options.Filter = context =>
                !StaticAssetFilter.IsStaticAsset(context.Request.Path) &&
                !HealthCheckEndpoints.IsHealthCheck(context.Request.Path);
        });
        builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options =>
            options.RecordException = true);

        // Ship exceptions/errors to the backend but not the per-request info chatter,
        // unless QATRACKER_TELEMETRY_LOG_LEVEL says otherwise.
        builder.Logging.AddFilter<OpenTelemetry.Logs.OpenTelemetryLoggerProvider>(
            category: null, TelemetryOptions.ResolveExportLogLevel(builder.Configuration));

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName,
                    serviceVersion: ServiceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes([
                    new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName),
                ]));

        // Npgsql 6+ emits its own "Npgsql" ActivitySource / Meter — this is all
        // Npgsql.OpenTelemetry's AddNpgsql() does. The Azure Monitor distro only
        // auto-instruments Microsoft.Data.SqlClient, so add it here for both paths.
        otel.WithTracing(tracing => tracing.AddSource("Npgsql"));

        var exportSummary = provider switch
        {
            TelemetryProvider.AzureMonitor => AddAzureMonitor(builder, otel),
            _ => AddOtlp(builder, otel),
        };

        return $"{exportSummary}, service={serviceName}";
    }

    private static string AddAzureMonitor(WebApplicationBuilder builder, OpenTelemetryBuilder otel)
    {
        var settings = TelemetryOptions.ResolveAzureMonitor(builder.Configuration);
        otel.UseAzureMonitor(options => options.ConnectionString = settings.ConnectionString);
        return "Azure Monitor";
    }

    private static string AddOtlp(WebApplicationBuilder builder, OpenTelemetryBuilder otel)
    {
        var settings = TelemetryOptions.ResolveOtlp(builder.Configuration);

        otel.WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Npgsql"))
            .WithLogging();

        var protocol = settings.Protocol == "http/protobuf"
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
        otel.UseOtlpExporter(protocol, new Uri(settings.Endpoint));

        return $"OTLP {settings.Endpoint} ({settings.Protocol})";
    }
}
