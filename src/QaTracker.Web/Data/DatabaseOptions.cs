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
/// </summary>
public static class DatabaseOptions
{
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
        };

        return builder.ConnectionString;
    }
}
