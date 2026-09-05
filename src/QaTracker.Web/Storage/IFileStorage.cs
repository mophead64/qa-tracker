namespace QaTracker.Web.Storage;

/// <summary>
/// Object storage for uploaded attachments. Backed by either S3 (AWS or an S3-compatible
/// service such as MinIO/Garage) or Azure Blob Storage — never both; see
/// <see cref="StorageOptions"/>. The app never links directly to the storage account:
/// every read goes through <c>AttachmentEndpoints</c> as a proxy.
/// </summary>
public interface IFileStorage
{
    /// <summary>True when a provider is actually configured; false disables uploads.</summary>
    bool IsConfigured { get; }

    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>Best-effort delete — swallows "not found" so callers don't need to check first.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
