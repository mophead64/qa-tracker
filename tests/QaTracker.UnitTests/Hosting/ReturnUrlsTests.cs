using QaTracker.Web;

namespace QaTracker.UnitTests.Hosting;

/// <summary>The shared same-site check on every posted returnUrl.</summary>
public sealed class ReturnUrlsTests
{
    [Theory]
    [InlineData("/", true)]
    [InlineData("/projects/1?commentsPage=2#comments", true)]
    [InlineData("//evil.example/x", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("projects/1", false)]
    [InlineData("/projects\r\nLocation: https://evil.example", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_same_site_paths_are_local(string? url, bool local) =>
        Assert.Equal(local, ReturnUrls.IsLocal(url));

    [Fact]
    public void Anything_else_falls_back()
    {
        Assert.Equal("/projects", ReturnUrls.LocalOr("/projects"));
        Assert.Equal("/", ReturnUrls.LocalOr("//evil.example"));
        Assert.Equal("/admin/tags", ReturnUrls.LocalOr("/\\evil.example", "/admin/tags"));
    }
}
