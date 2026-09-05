using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using QaTracker.Web.Data;

namespace QaTracker.Web.Hosting;

/// <summary>
/// Readiness check: is PostgreSQL reachable right now? Uses a dedicated short-timeout,
/// unpooled connection that bypasses EF's retry strategy, so the probe fails in a few
/// seconds rather than the tens of seconds an EF <c>CanConnect</c> spends exhausting its
/// retry budget. Registered under <see cref="HealthCheckEndpoints.ReadyTag"/>.
/// </summary>
public sealed class DatabaseReadyHealthCheck(IConfiguration configuration) : IHealthCheck
{
    private readonly string _connectionString = BuildProbeConnectionString(configuration);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", ex);
        }
    }

    private static string BuildProbeConnectionString(IConfiguration configuration) =>
        new NpgsqlConnectionStringBuilder(DatabaseOptions.ResolveConnectionString(configuration))
        {
            Timeout = 3,
            CommandTimeout = 3,
            Pooling = false,
            ApplicationName = "qatracker-healthcheck",
        }.ConnectionString;
}
