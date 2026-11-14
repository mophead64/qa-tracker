using QaTracker.Web.Storage;

namespace QaTracker.UnitTests.Storage;

/// <summary>The S3 client settings: requests are signed for the configured region, endpoint or not.</summary>
public sealed class S3FileStorageTests
{
    private static S3StorageSettings Settings(string region, string? serviceUrl) =>
        new("bucket", region, serviceUrl, "access-key", "secret-key-of-16+", ForcePathStyle: serviceUrl is not null);

    [Fact]
    public void A_custom_endpoint_signs_for_the_configured_region_not_us_east_1()
    {
        // Garage / MinIO with their own region name: signing for us-east-1 is rejected.
        var config = S3FileStorage.BuildConfig(Settings("garage", "http://garage:3900"));

        Assert.Equal("garage", config.AuthenticationRegion);
        Assert.Equal("http://garage:3900/", config.ServiceURL);
        Assert.True(config.ForcePathStyle);
    }

    [Fact]
    public void Real_aws_uses_the_region_endpoint()
    {
        var config = S3FileStorage.BuildConfig(Settings("eu-west-2", null));

        Assert.Equal("eu-west-2", config.RegionEndpoint?.SystemName);
        Assert.Null(config.ServiceURL);
    }
}
