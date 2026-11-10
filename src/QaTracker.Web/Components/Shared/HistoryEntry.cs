using Microsoft.AspNetCore.Components;

namespace QaTracker.Web.Components.Shared;

/// <summary>One row of a <see cref="HistoryTimeline"/>: who, when, and what (already phrased by
/// the caller). <paramref name="Kind"/> is emitted as <c>data-history-kind</c> for tests;
/// <paramref name="DotClass"/> colours the dot (e.g. <c>timeline-red</c>), empty for neutral.</summary>
public sealed record HistoryEntry(
    Guid Id,
    string Kind,
    string ActorName,
    DateTimeOffset OccurredUtc,
    string DotClass,
    RenderFragment Description);
