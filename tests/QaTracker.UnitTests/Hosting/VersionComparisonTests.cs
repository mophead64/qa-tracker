using QaTracker.Web.Hosting.UpdateCheck;

namespace QaTracker.UnitTests.Hosting;

public sealed class VersionComparisonTests
{
    [Theory]
    [InlineData("2026.09.10", "2026.09.09")]      // later day
    [InlineData("v2026.09.10", "2026.09.09")]     // leading v ignored
    [InlineData("2026.10.01", "2026.09.30")]      // month rollover
    [InlineData("2027.01.01", "2026.12.31")]      // year rollover
    [InlineData("2026.09.09.1", "2026.09.09")]    // same-day second release
    [InlineData("2026.09.09.2", "2026.09.09.1")]  // same-day third release
    [InlineData("v2026.09.10-rc1", "2026.09.09")] // prerelease suffix stripped
    [InlineData("2026.09.10+build.7", "2026.09.09")] // build metadata stripped
    public void IsNewer_true_when_candidate_is_ahead(string candidate, string current)
    {
        Assert.True(VersionComparison.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("2026.09.09", "2026.09.09")]      // equal
    [InlineData("2026.09.08", "2026.09.09")]      // older
    [InlineData("2026.09.09", "2026.09.09.1")]    // missing components count as 0
    [InlineData("v2026.09.09", "v2026.09.09")]    // equal with v
    public void IsNewer_false_when_not_ahead(string candidate, string current)
    {
        Assert.False(VersionComparison.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("Development", "2026.09.09")]
    [InlineData("2026.09.09", "unknown")]
    [InlineData("", "2026.09.09")]
    [InlineData(null, "2026.09.09")]
    [InlineData("2026.09.09", null)]
    [InlineData("main-abc123", "2026.09.09")]
    public void IsNewer_false_when_either_side_is_unparseable(string? candidate, string? current)
    {
        Assert.False(VersionComparison.IsNewer(candidate, current));
    }
}
