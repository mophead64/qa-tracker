namespace QaTracker.Web.Storage;

/// <summary>
/// Registered when no storage provider is configured. Reports <see cref="IsConfigured"/>
/// as false so the UI can hide upload controls; throws if used anyway, since that would
/// mean the UI check was bypassed.
/// </summary>
public sealed class NullFileStorage : IFileStorage
{
    public bool IsConfigured => false;

    public Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default) =>
        throw new InvalidOperationException("File storage is not configured (QATRACKER_STORAGE_PROVIDER is unset).");

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        throw new InvalidOperationException("File storage is not configured (QATRACKER_STORAGE_PROVIDER is unset).");

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        throw new InvalidOperationException("File storage is not configured (QATRACKER_STORAGE_PROVIDER is unset).");
}
