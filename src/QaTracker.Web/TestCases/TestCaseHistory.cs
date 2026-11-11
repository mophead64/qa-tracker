using QaTracker.Web.Data;

namespace QaTracker.Web.TestCases;

/// <summary>A history entry as shown on the timeline, with the actor's display name resolved.</summary>
public sealed record TestCaseEventView(
    Guid Id,
    TestCaseEventKind Kind,
    string? ActorId,
    string ActorName,
    DateTimeOffset OccurredUtc,
    string? FromValue,
    string? ToValue)
{
    /// <summary>The previous result of a <see cref="TestCaseEventKind.ResultChanged"/> entry, if known.</summary>
    public TestResult? FromResult => ParseResult(FromValue);

    /// <summary>The new result of a <see cref="TestCaseEventKind.ResultChanged"/> entry.</summary>
    public TestResult? ToResult => ParseResult(ToValue);

    private static TestResult? ParseResult(string? value) =>
        Enum.TryParse<TestResult>(value, out var result) ? result : null;
}

/// <summary>Adds <see cref="TestCaseEvent"/> rows to a context; the caller's SaveChanges commits them
/// with the change they describe.</summary>
public static class TestCaseHistory
{
    /// <summary>What an <see cref="TestCaseEventKind.Edited"/> entry says changed.</summary>
    public const string EditedScenario = "scenario";
    public const string EditedSteps = "steps";
    public const string EditedScenarioAndSteps = "scenario and steps";

    public static void Record(
        ApplicationDbContext db,
        Guid testCaseId,
        TestCaseEventKind kind,
        string? actorId,
        DateTimeOffset occurredUtc,
        string? fromValue = null,
        string? toValue = null)
    {
        db.TestCaseEvents.Add(new TestCaseEvent
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Kind = kind,
            ActorId = string.IsNullOrEmpty(actorId) ? null : actorId,
            OccurredUtc = occurredUtc,
            FromValue = Truncate(fromValue),
            ToValue = Truncate(toValue),
        });
    }

    private static string? Truncate(string? value) =>
        value is { Length: > 500 } ? value[..500] : value;
}
