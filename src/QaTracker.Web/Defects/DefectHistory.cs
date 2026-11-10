using QaTracker.Web.Data;

namespace QaTracker.Web.Defects;

/// <summary>A defect history entry as shown on the timeline, with the actor's name resolved.</summary>
public sealed record DefectEventView(
    Guid Id,
    DefectEventKind Kind,
    string? ActorId,
    string ActorName,
    DateTimeOffset OccurredUtc,
    string? FromValue,
    string? ToValue)
{
    public DefectStatus? FromStatus => Parse<DefectStatus>(FromValue);
    public DefectStatus? ToStatus => Parse<DefectStatus>(ToValue);
    public DefectSeverity? FromSeverity => Parse<DefectSeverity>(FromValue);
    public DefectSeverity? ToSeverity => Parse<DefectSeverity>(ToValue);

    private static T? Parse<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) ? parsed : null;
}

/// <summary>Adds <see cref="DefectEvent"/> rows to a context; the caller's SaveChanges commits them
/// with the change they describe.</summary>
public static class DefectHistory
{
    public static void Record(
        ApplicationDbContext db,
        Guid defectId,
        DefectEventKind kind,
        string? actorId,
        DateTimeOffset occurredUtc,
        string? fromValue = null,
        string? toValue = null)
    {
        db.DefectEvents.Add(new DefectEvent
        {
            Id = Guid.NewGuid(),
            DefectId = defectId,
            Kind = kind,
            ActorId = string.IsNullOrEmpty(actorId) ? null : actorId,
            OccurredUtc = occurredUtc,
            FromValue = Truncate(fromValue),
            ToValue = Truncate(toValue),
        });
    }

    /// <summary>"summary", "repro steps and actual results", "summary, expected results and actual results" …</summary>
    public static string? DescribeEditedFields(IReadOnlyList<string> fields) => fields.Count switch
    {
        0 => null,
        1 => fields[0],
        _ => string.Join(", ", fields.Take(fields.Count - 1)) + " and " + fields[^1],
    };

    /// <summary>Display order for entries recorded in the same instant (one workflow action can
    /// change status and assignee together): the status change reads first, creation last.</summary>
    public static int SameInstantRank(DefectEventKind kind) => kind switch
    {
        DefectEventKind.StatusChanged => 0,
        DefectEventKind.AssigneeChanged => 1,
        DefectEventKind.SeverityChanged => 2,
        DefectEventKind.Edited => 3,
        DefectEventKind.Created => 9,
        _ => 5,
    };

    private static string? Truncate(string? value) =>
        value is { Length: > 500 } ? value[..500] : value;
}
