using Npgsql;

namespace QaTracker.Web.Data;

/// <summary>
/// Resolves the PostgreSQL connection string.
///
/// Order of precedence:
///   1. ConnectionStrings:DefaultConnection (env var: ConnectionStrings__DefaultConnection)
///   2. Discrete QATRACKER_DB_* settings, assembled into a connection string.
///
/// Secrets are expected to arrive via environment variables (a .env file in local dev,
/// Key Vault references on Azure, etc.) rather than being committed to appsettings.
///
/// On the assembled path the connection pool is bounded by QATRACKER_DB_MAX_POOL_SIZE
/// (default 20) / QATRACKER_DB_MIN_POOL_SIZE (default 1). Budget across instances:
/// <c>instances * MaxPoolSize + headroom &lt; postgres max_connections</c>. An explicit
/// ConnectionStrings:DefaultConnection is passed through verbatim — the operator owns it.
/// </summary>
public static class DatabaseOptions
{
    private const int DefaultMaxPoolSize = 20;
    private const int DefaultMinPoolSize = 1;

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var explicitConnection = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(explicitConnection))
        {
            return explicitConnection;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = configuration["QATRACKER_DB_HOST"] ?? "localhost",
            Port = int.TryParse(configuration["QATRACKER_DB_PORT"], out var port) ? port : 5432,
            Database = configuration["QATRACKER_DB_NAME"] ?? "qatracker",
            Username = configuration["QATRACKER_DB_USER"] ?? "qatracker",
            Password = configuration["QATRACKER_DB_PASSWORD"] ?? "qatracker",
            MaxPoolSize = int.TryParse(configuration["QATRACKER_DB_MAX_POOL_SIZE"], out var maxPool)
                ? maxPool
                : DefaultMaxPoolSize,
            MinPoolSize = int.TryParse(configuration["QATRACKER_DB_MIN_POOL_SIZE"], out var minPool)
                ? minPool
                : DefaultMinPoolSize,
        };

        return builder.ConnectionString;
    }
}
