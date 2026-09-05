using QaTracker.Web.Telemetry;

namespace QaTracker.UnitTests.Telemetry;

public class StaticAssetFilterTests
{
    [Theory]
    [InlineData("/app.css")]
    [InlineData("/QaTracker.Web.styles.css")]
    [InlineData("/favicon.png")]
    [InlineData("/js/site.js")]
    [InlineData("/lib/htmx/htmx.min.js")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_framework/dotnet.runtime.js")]
    [InlineData("/_content/SomePackage/styles.css")]
    [InlineData("/_blazor")]
    [InlineData("/_blazor/negotiate")]
    [InlineData("/media/logo.svg")]
    [InlineData("/fonts/inter.woff2")]
    public void Treats_asset_requests_as_static(string path)
    {
        Assert.True(StaticAssetFilter.IsStaticAsset(path));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/admin")]
    [InlineData("/projects/42/defects/7")]
    [InlineData("/Account/Login")]
    [InlineData("/attachments/9/download")]
    [InlineData("")]
    [InlineData(null)]
    public void Treats_page_and_api_requests_as_non_static(string? path)
    {
        Assert.False(StaticAssetFilter.IsStaticAsset(path));
    }
}
