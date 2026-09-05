using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace QaTracker.Web.Storage;

/// <summary>Stores attachments as blobs in a single private Azure Storage container.</summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient container;
    private readonly Lazy<Task> ensureContainer;

    public AzureBlobFileStorage(AzureStorageSettings settings)
    {
        container = new BlobContainerClient(settings.ConnectionString, settings.Container);
        ensureContainer = new Lazy<Task>(() => container.CreateIfNotExistsAsync(PublicAccessType.None));
    }

    public bool IsConfigured => true;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await ensureContainer.Value;
        var blob = container.GetBlobClient(key);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var blob = container.GetBlobClient(key);
        var download = await blob.DownloadStreamingAsync(cancellationToken: ct);
        return download.Value.Content;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var blob = container.GetBlobClient(key);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }
}
