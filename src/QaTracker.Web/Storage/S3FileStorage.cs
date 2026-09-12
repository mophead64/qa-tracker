using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace QaTracker.Web.Storage;

/// <summary>
/// Stores attachments in an S3 bucket. Works against real AWS S3 or any S3-compatible
/// service (MinIO, Garage) by pointing <see cref="S3StorageSettings.ServiceUrl"/> at it.
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client client;
    private readonly string bucket;

    public S3FileStorage(S3StorageSettings settings)
    {
        bucket = settings.Bucket;

        var config = new AmazonS3Config
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(settings.Region),
            ForcePathStyle = settings.ForcePathStyle,
            // AWSSDK.S3 v4 defaults to always computing/validating a flexible checksum,
            // which it sends as an aws-chunked trailer — Garage (and this has also bitten
            // other S3-compatible servers) rejects that as "Invalid payload signature".
            // WHEN_REQUIRED only adds a checksum for operations that actually need one
            // (e.g. multipart complete), which every backend handles fine, real AWS S3
            // included.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (settings.ServiceUrl is not null)
        {
            config.ServiceURL = settings.ServiceUrl;
        }

        client = new AmazonS3Client(settings.AccessKey, settings.SecretKey, config);
    }

    public bool IsConfigured => true;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        }, ct);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var response = await client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucket,
            Key = key,
        }, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await client.DeleteObjectAsync(bucket, key, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone — nothing to clean up.
        }
    }

    public void Dispose() => client.Dispose();
}
