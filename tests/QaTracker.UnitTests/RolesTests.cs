using QaTracker.Web.Data;

namespace QaTracker.UnitTests;

public class RolesTests
{
    [Fact]
    public void All_contains_qa_and_dev()
    {
        Assert.Equal(["QA", "Dev"], Roles.All);
    }
}
