namespace QaTracker.Web.Dashboard;

/// <summary>Presentation helpers for <see cref="TestCaseFollowUpReason"/>.</summary>
public static class TestCaseFollowUpDisplay
{
    public static string Label(TestCaseFollowUpReason reason) => reason switch
    {
        TestCaseFollowUpReason.PassedWithOpenDefect => "Passed, open defect",
        TestCaseFollowUpReason.FailedNoDefect => "Failed, no defect raised",
        TestCaseFollowUpReason.FailedReadyToRetest => "Ready to retest",
        _ => reason.ToString(),
    };

    public static string BadgeClass(TestCaseFollowUpReason reason) => reason switch
    {
        TestCaseFollowUpReason.PassedWithOpenDefect => "badge badge-amber",
        TestCaseFollowUpReason.FailedNoDefect => "badge badge-red",
        TestCaseFollowUpReason.FailedReadyToRetest => "badge badge-brand",
        _ => "badge badge-gray",
    };
}
