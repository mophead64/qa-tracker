using QaTracker.Web.Data;

namespace QaTracker.UnitTests;

public class RolesTests
{
    [Fact]
    public void All_lists_every_role_most_privileged_first()
    {
        Assert.Equal(["QA", "Dev", "Viewer"], Roles.All);
        Assert.Equal(["QA", "Dev"], Roles.Contributors);
    }

    [Theory]
    [InlineData(new[] { "Viewer", "Dev" }, "Dev")]
    [InlineData(new[] { "Dev", "QA" }, "QA")]
    [InlineData(new[] { "Viewer" }, "Viewer")]
    [InlineData(new[] { "Something else" }, null)]
    [InlineData(new string[0], null)]
    public void MostPrivileged_picks_the_strongest_known_role(string[] roles, string? expected) =>
        Assert.Equal(expected, Roles.MostPrivileged(roles));
}
