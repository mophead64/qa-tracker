namespace QaTracker.Web.Storage;

public static class FileStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IFileStorage"/> implementation selected by
    /// <c>QATRACKER_STORAGE_PROVIDER</c>, or a <see cref="NullFileStorage"/> (uploads
    /// disabled) when it's unset.
    /// </summary>
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = StorageOptions.ResolveProvider(configuration);
        services.AddSingleton<IFileStorage>(sp => provider switch
        {
            StorageProvider.S3 => new S3FileStorage(StorageOptions.ResolveS3(configuration)),
            StorageProvider.Azure => new AzureBlobFileStorage(StorageOptions.ResolveAzure(configuration)),
            _ => LogDisabledAndReturnNull(sp),
        });

        return services;
    }

    private static NullFileStorage LogDisabledAndReturnNull(IServiceProvider sp)
    {
        sp.GetRequiredService<ILogger<NullFileStorage>>()
            .LogWarning("QATRACKER_STORAGE_PROVIDER is not set — file uploads are disabled.");
        return new NullFileStorage();
    }
}
