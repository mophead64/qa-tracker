using Microsoft.Extensions.Configuration;
using QaTracker.Web.Storage;

namespace QaTracker.UnitTests.Storage;

/// <summary>How QATRACKER_STORAGE_* / QATRACKER_S3_* / QATRACKER_AZURE_* settings resolve.</summary>
public sealed class StorageOptionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Theory]
    [InlineData("S3", StorageProvider.S3)]
    [InlineData(" s3 ", StorageProvider.S3)]
    [InlineData("azure", StorageProvider.Azure)]
    [InlineData("", StorageProvider.None)]
    [InlineData("gcs", StorageProvider.None)]
    [InlineData(null, StorageProvider.None)]
    public void The_provider_is_case_insensitive_and_anything_else_is_none(string? value, StorageProvider expected) =>
        Assert.Equal(expected, StorageOptions.ResolveProvider(Config(("QATRACKER_STORAGE_PROVIDER", value))));

    [Fact]
    public void Real_s3_defaults_the_region_and_uses_virtual_hosted_style()
    {
        var s3 = StorageOptions.ResolveS3(Config(
            ("QATRACKER_S3_BUCKET", "files"), ("QATRACKER_S3_ACCESS_KEY", "a"), ("QATRACKER_S3_SECRET_KEY", "s")));

        Assert.Equal(new S3StorageSettings("files", "us-east-1", null, "a", "s", ForcePathStyle: false), s3);
    }

    [Fact]
    public void A_custom_endpoint_turns_path_style_on_unless_told_otherwise()
    {
        (string, string?)[] common = [("QATRACKER_S3_BUCKET", "b"), ("QATRACKER_S3_ACCESS_KEY", "a"), ("QATRACKER_S3_SECRET_KEY", "s"),
            ("QATRACKER_S3_ENDPOINT", "http://garage:3900"), ("QATRACKER_S3_REGION", "garage")];

        var defaulted = StorageOptions.ResolveS3(Config(common));
        Assert.True(defaulted.ForcePathStyle);
        Assert.Equal("http://garage:3900", defaulted.ServiceUrl);
        Assert.Equal("garage", defaulted.Region);

        var overridden = StorageOptions.ResolveS3(Config([.. common, ("QATRACKER_S3_FORCE_PATH_STYLE", "false")]));
        Assert.False(overridden.ForcePathStyle);
    }

    [Theory]
    [InlineData("QATRACKER_S3_BUCKET")]
    [InlineData("QATRACKER_S3_ACCESS_KEY")]
    [InlineData("QATRACKER_S3_SECRET_KEY")]
    public void Missing_required_s3_settings_fail_loudly_and_name_the_variable(string missing)
    {
        var values = new[] { "QATRACKER_S3_BUCKET", "QATRACKER_S3_ACCESS_KEY", "QATRACKER_S3_SECRET_KEY" }
            .Where(k => k != missing).Select(k => (k, (string?)"x")).ToArray();

        var error = Assert.Throws<InvalidOperationException>(() => StorageOptions.ResolveS3(Config(values)));
        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public void Azure_defaults_the_container_and_requires_the_connection_string()
    {
        var azure = StorageOptions.ResolveAzure(Config(("QATRACKER_AZURE_STORAGE_CONNECTION_STRING", "UseDevelopmentStorage=true")));
        Assert.Equal("qatracker-attachments", azure.Container);

        Assert.Throws<InvalidOperationException>(() => StorageOptions.ResolveAzure(Config()));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData("5", 5)]
    [InlineData("not a number", 20)]
    public void The_upload_limit_is_in_megabytes(string? value, long mb) =>
        Assert.Equal(mb * 1024 * 1024, StorageOptions.ResolveMaxUploadBytes(Config(("QATRACKER_MAX_UPLOAD_MB", value))));
}
