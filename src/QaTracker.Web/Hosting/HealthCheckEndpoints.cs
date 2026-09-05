namespace QaTracker.Web.Hosting;

/// <summary>
/// Liveness / readiness endpoint paths and a matcher so request logging and telemetry can
/// keep the platform's probe traffic out of the signal (same intent as
/// <see cref="QaTracker.Web.Telemetry.StaticAssetFilter"/>).
///
/// <c>/health/live</c> reports only that the process is up (no dependencies) — a failing DB
/// must not restart-loop the fleet. <c>/health/ready</c> also checks the database and is what
/// gates traffic.
/// </summary>
public static class HealthCheckEndpoints
{
    public const string Live = "/health/live";
    public const string Ready = "/health/ready";
    public const string ReadyTag = "ready";

    public static bool IsHealthCheck(string? path) =>
        !string.IsNullOrEmpty(path) &&
        (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase));
}
