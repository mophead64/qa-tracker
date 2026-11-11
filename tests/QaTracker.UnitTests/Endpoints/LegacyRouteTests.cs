using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using QaTracker.Web;
using QaTracker.Web.Hosting;

namespace QaTracker.UnitTests.Endpoints;

/// <summary>Old (pre-November-2026) URLs permanently redirect to their new shapes.</summary>
public sealed class LegacyRouteTests : IAsyncLifetime
{
    private static readonly Guid P = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid S = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid T = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private WebApplication app = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        app = builder.Build();
        app.MapLegacyRedirects();
        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }

    public static TheoryData<string, string> Redirects => new()
    {
        { $"/projects/{P}/test-cases/scopes/{S}/cases/{T}", $"/projects/{P}/test-cases/{T}" },
        { $"/projects/{P}/test-cases/scopes/{S}/cases/{T}/edit", $"/projects/{P}/test-cases/{T}/edit" },
        { $"/projects/{P}/test-cases/scopes/{S}/cases/new?fromDefect=abc", $"/projects/{P}/scopes/{S}/test-cases/new?fromDefect=abc" },
        { $"/projects/{P}/test-cases/scopes/new", $"/projects/{P}/scopes/new" },
        { $"/projects/{P}/test-cases/scopes/{S}", $"/projects/{P}/scopes/{S}" },
        { $"/projects/{P}/test-cases/scopes/{S}/edit", $"/projects/{P}/scopes/{S}/edit" },
        { $"/projects/{P}/test-cases.csv?results=true", $"/projects/{P}/test-cases/export.csv?results=true" },
    };

    [Theory]
    [MemberData(nameof(Redirects))]
    public async Task Old_urls_permanently_redirect(string oldUrl, string newUrl)
    {
        var response = await client.GetAsync(oldUrl);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(newUrl, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public void App_urls_follow_the_project_resource_shape()
    {
        Assert.Equal($"projects/{P}/test-cases/{T}", AppUrls.TestCase(P, T));
        Assert.Equal($"projects/{P}/test-cases/{T}/edit", AppUrls.EditTestCase(P, T));
        Assert.Equal($"projects/{P}/test-cases/{T}/result", AppUrls.TestCaseAction(P, T, "result"));
        Assert.Equal($"projects/{P}/scopes/{S}", AppUrls.Scope(P, S));
        Assert.Equal($"projects/{P}/scopes/{S}/test-cases/new", AppUrls.NewTestCase(P, S));
        Assert.Equal($"projects/{P}/test-cases/export.csv?results=true", AppUrls.ExportTestCases(P, withResults: true));
        Assert.Equal($"/projects/{P}/defects/{T}", AppUrls.Absolute(AppUrls.Defect(P, T)));
    }
}
