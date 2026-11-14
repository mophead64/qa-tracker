using QaTracker.Web.Projects;

namespace QaTracker.UnitTests.Projects;

public sealed class ProjectOrderingTests
{
    private static Project P(string name, ProjectStatus status, int? startDay = null) => new()
    {
        Name = name,
        Status = status,
        TestingStartDate = startDay is { } d ? new DateOnly(2026, 12, d) : null,
    };

    [Fact]
    public void Active_and_upcoming_lead_by_start_date_and_the_rest_follow_by_name()
    {
        var ordered = ProjectOrdering.Order(
        [
            P("zeta cancelled", ProjectStatus.Cancelled),
            P("b done", ProjectStatus.Complete, 1),
            P("a done", ProjectStatus.Complete, 20),
            P("Fresh", ProjectStatus.NotStarted),
            P("Soon (undated)", ProjectStatus.Upcoming),
            P("Soon", ProjectStatus.Upcoming, 2),
            P("Active later", ProjectStatus.InFlight, 15),
            P("Active sooner", ProjectStatus.InFlight, 10),
        ]);

        Assert.Equal(
            ["Active sooner", "Active later", "Soon", "Soon (undated)", "Fresh", "a done", "b done", "zeta cancelled"],
            ordered.Select(p => p.Name));
    }
}
