using QaTracker.Web.Hosting;

namespace QaTracker.UnitTests.Hosting;

public class HealthCheckEndpointsTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    [InlineData("/health/anything")]
    [InlineData("/HEALTH/LIVE")]
    public void Recognises_health_paths(string path)
    {
        Assert.True(HealthCheckEndpoints.IsHealthCheck(path));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/healthz")]
    [InlineData("/api/health")]
    [InlineData("/projects/42")]
    [InlineData("")]
    [InlineData(null)]
    public void Ignores_other_paths(string? path)
    {
        Assert.False(HealthCheckEndpoints.IsHealthCheck(path));
    }
}
