namespace QaTracker.Web.Storage;

/// <summary>Which object storage backend is configured. The system is one or the other, never both.</summary>
public enum StorageProvider
{
    None,
    S3,
    Azure,
}

/// <summary>Resolved S3 settings — works against real AWS S3 or an S3-compatible service (MinIO, Garage).</summary>
public sealed record S3StorageSettings(
    string Bucket,
    string Region,
    string? ServiceUrl,
    string AccessKey,
    string SecretKey,
    bool ForcePathStyle);

/// <summary>Resolved Azure Blob Storage settings.</summary>
public sealed record AzureStorageSettings(string ConnectionString, string Container);

/// <summary>
/// Resolves which storage backend is configured from <c>QATRACKER_STORAGE_PROVIDER</c>
/// plus its provider-specific env vars, mirroring the precedence style of
/// <see cref="Data.DatabaseOptions"/>.
/// </summary>
public static class StorageOptions
{
    public static StorageProvider ResolveProvider(IConfiguration configuration) =>
        configuration["QATRACKER_STORAGE_PROVIDER"]?.Trim().ToUpperInvariant() switch
        {
            "S3" => StorageProvider.S3,
            "AZURE" => StorageProvider.Azure,
            _ => StorageProvider.None,
        };

    public static S3StorageSettings ResolveS3(IConfiguration configuration)
    {
        var endpoint = configuration["QATRACKER_S3_ENDPOINT"];
        return new S3StorageSettings(
            Bucket: Require(configuration, "QATRACKER_S3_BUCKET"),
            Region: configuration["QATRACKER_S3_REGION"] ?? "us-east-1",
            ServiceUrl: string.IsNullOrWhiteSpace(endpoint) ? null : endpoint,
            AccessKey: Require(configuration, "QATRACKER_S3_ACCESS_KEY"),
            SecretKey: Require(configuration, "QATRACKER_S3_SECRET_KEY"),
            // MinIO/Garage need path-style addressing; default that on whenever a custom
            // endpoint is set (real AWS S3 doesn't set QATRACKER_S3_ENDPOINT).
            ForcePathStyle: bool.TryParse(configuration["QATRACKER_S3_FORCE_PATH_STYLE"], out var forcePathStyle)
                ? forcePathStyle
                : !string.IsNullOrWhiteSpace(endpoint));
    }

    public static AzureStorageSettings ResolveAzure(IConfiguration configuration) =>
        new(
            ConnectionString: Require(configuration, "QATRACKER_AZURE_STORAGE_CONNECTION_STRING"),
            Container: configuration["QATRACKER_AZURE_CONTAINER"] ?? "qatracker-attachments");

    /// <summary>Maximum upload size, in bytes, from <c>QATRACKER_MAX_UPLOAD_MB</c> (default 20 MB).</summary>
    public static long ResolveMaxUploadBytes(IConfiguration configuration)
    {
        var mb = int.TryParse(configuration["QATRACKER_MAX_UPLOAD_MB"], out var configured) ? configured : 20;
        return mb * 1024L * 1024L;
    }

    private static string Require(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required when QATRACKER_STORAGE_PROVIDER is set.");
}
