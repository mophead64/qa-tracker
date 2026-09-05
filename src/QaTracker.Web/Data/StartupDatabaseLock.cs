using Npgsql;

namespace QaTracker.Web.Data;

/// <summary>
/// Serialises database initialisation across instances with a PostgreSQL session-level
/// advisory lock. When several instances start at once (a rolling deploy, or the platform
/// scaling out), exactly one applies migrations and seeds; the rest block in
/// <see cref="AcquireAsync"/> until it finishes, then run their now-no-op initialisation.
///
/// The lock is held on a dedicated connection kept open for the lock's lifetime, outside
/// the EF context pool. Session-level advisory locks release automatically if that
/// connection drops, so a process that crashes mid-migration cannot wedge later starts.
/// </summary>
internal sealed class StartupDatabaseLock : IAsyncDisposable
{
    // Arbitrary but STABLE 64-bit key shared by every instance. Never change it.
    private const long LockKey = 0x5141_5452_4B52_0001;

    private readonly NpgsqlConnection _connection;
    private readonly ILogger _logger;

    private StartupDatabaseLock(NpgsqlConnection connection, ILogger logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public static async Task<StartupDatabaseLock> AcquireAsync(
        string connectionString, ILogger logger, CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        logger.LogInformation("Waiting for the startup database lock...");
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection))
        {
            command.Parameters.AddWithValue("key", LockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        logger.LogInformation("Acquired the startup database lock.");

        return new StartupDatabaseLock(connection, logger);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", _connection);
            command.Parameters.AddWithValue("key", LockKey);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not explicitly release the startup database lock; it releases when the connection closes.");
        }

        await _connection.DisposeAsync();
    }
}
