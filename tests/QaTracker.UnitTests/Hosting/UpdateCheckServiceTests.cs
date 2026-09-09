using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.Web.Hosting;
using QaTracker.Web.Hosting.UpdateCheck;

namespace QaTracker.UnitTests.Hosting;

public sealed class UpdateCheckServiceTests
{
    private static BuildInfo Build(string version = "2026.09.09", bool enabled = true) =>
        new(version, "master", "abcdef1234567890", "abcdef1", null, BuildInfo.DefaultGitHubRepo, enabled);

    private static string ReleaseJson(string tag, string name = "Release", string url = "https://example/r") =>
        $$"""{"tag_name":"{{tag}}","name":"{{name}}","html_url":"{{url}}"}""";

    private static (UpdateCheckService Service, StubHandler Handler) Make(
        BuildInfo build, FakeTimeProvider? time = null, params (HttpStatusCode Status, string? Body)[] responses)
    {
        var handler = new StubHandler(responses);
        var factory = new StubHttpClientFactory(handler);
        var service = new UpdateCheckService(
            factory, build, time ?? new FakeTimeProvider(), NullLogger<UpdateCheckService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task Reports_update_available_when_the_release_is_newer()
    {
        var (service, handler) = Make(Build(), responses:
            (HttpStatusCode.OK, ReleaseJson("v2026.09.10", "September", "https://gh/release/1")));

        var status = await service.CheckAsync();

        Assert.Equal(UpdateState.UpdateAvailable, status.State);
        Assert.Equal("v2026.09.10", status.LatestVersion);
        Assert.Equal("https://gh/release/1", status.ReleaseUrl);
        Assert.NotNull(status.CheckedAt);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(UpdateState.UpdateAvailable, service.Current.State);
    }

    [Fact]
    public async Task Reports_up_to_date_when_the_release_is_not_newer()
    {
        var (service, _) = Make(Build("2026.09.10"), responses:
            (HttpStatusCode.OK, ReleaseJson("v2026.09.10")));

        Assert.Equal(UpdateState.UpToDate, (await service.CheckAsync()).State);
    }

    [Fact]
    public async Task Reports_up_to_date_when_the_repo_exists_but_has_no_releases()
    {
        // releases/latest -> 404, then the repo probe -> 200.
        var (service, handler) = Make(Build(), responses:
        [
            (HttpStatusCode.NotFound, null),
            (HttpStatusCode.OK, """{"full_name":"mophead64/qa-tracker"}"""),
        ]);

        Assert.Equal(UpdateState.UpToDate, (await service.CheckAsync()).State);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Reports_check_failed_when_the_configured_repo_does_not_exist()
    {
        // Both releases/latest and the repo probe 404.
        var (service, _) = Make(Build(), responses: (HttpStatusCode.NotFound, null));

        Assert.Equal(UpdateState.CheckFailed, (await service.CheckAsync()).State);
    }

    [Fact]
    public async Task A_server_error_is_a_check_failure_and_does_not_clobber_an_earlier_good_result()
    {
        var time = new FakeTimeProvider();
        var (service, handler) = Make(Build(), time, responses:
        [
            (HttpStatusCode.OK, ReleaseJson("v2026.09.10")),
            (HttpStatusCode.InternalServerError, null),
        ]);

        Assert.Equal(UpdateState.UpdateAvailable, (await service.CheckAsync()).State);

        time.Advance(TimeSpan.FromMinutes(5));
        var second = await service.CheckAsync();

        Assert.Equal(2, handler.Calls);
        Assert.Equal(UpdateState.UpdateAvailable, second.State);      // last good result kept
        Assert.Equal(UpdateState.UpdateAvailable, service.Current.State);
    }

    [Fact]
    public async Task Repeated_checks_within_the_throttle_window_hit_github_once()
    {
        var (service, handler) = Make(Build(), responses:
            (HttpStatusCode.OK, ReleaseJson("v2026.09.10")));

        await service.CheckAsync();
        await service.CheckAsync();
        await service.CheckAsync();

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Disabled_build_never_calls_github()
    {
        var (service, handler) = Make(Build(enabled: false), responses:
            (HttpStatusCode.OK, ReleaseJson("v2099.01.01")));

        Assert.Equal(UpdateState.Disabled, (await service.CheckAsync()).State);
        Assert.Equal(UpdateState.Disabled, service.Current.State);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class StubHandler((HttpStatusCode Status, string? Body)[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Math.Min(Calls, responses.Length - 1);
            Calls++;
            var (status, body) = responses[index];
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
