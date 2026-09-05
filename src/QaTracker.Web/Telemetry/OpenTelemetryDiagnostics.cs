using System.Diagnostics.Tracing;

namespace QaTracker.Web.Telemetry;

/// <summary>
/// Forwards the OpenTelemetry SDK's own internal <see cref="EventSource"/> output to
/// <see cref="ILogger"/> so a backend that isn't receiving data surfaces in the console
/// instead of failing silently. Kept deliberately narrow: exporter sources at Warning
/// (that's where transport failures land) and the core SDK source only at Error — the SDK
/// source is chatty at Warning (e.g. one line per ignored metric instrument). Registered
/// as a singleton and resolved once at startup; the DI container keeps it alive (an
/// unreferenced <see cref="EventListener"/> would be collected and stop listening).
/// </summary>
public sealed class OpenTelemetryDiagnostics : EventListener
{
    private readonly ILogger<OpenTelemetryDiagnostics> logger;
    private readonly List<EventSource> deferred = [];
    private readonly Lock gate = new();
    private bool ready;

    public OpenTelemetryDiagnostics(ILogger<OpenTelemetryDiagnostics> logger)
    {
        this.logger = logger;

        // OnEventSourceCreated fires inside the base constructor — before this field is
        // set — for sources that already exist, so those are queued and enabled here.
        lock (gate)
        {
            ready = true;
            foreach (var source in deferred)
            {
                Enable(source);
            }

            deferred.Clear();
        }
    }

    private static EventLevel? LevelFor(string sourceName) => sourceName switch
    {
        "OpenTelemetry-Sdk" or "OpenTelemetry-Extensions-Hosting" => EventLevel.Error,
        _ when sourceName.StartsWith("OpenTelemetry-Exporter", StringComparison.Ordinal) => EventLevel.Warning,
        _ => null,
    };

    private void Enable(EventSource source)
    {
        if (LevelFor(source.Name) is { } level)
        {
            EnableEvents(source, level);
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (LevelFor(eventSource.Name) is null)
        {
            return;
        }

        lock (gate)
        {
            if (ready)
            {
                Enable(eventSource);
            }
            else
            {
                deferred.Add(eventSource);
            }
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (LevelFor(eventData.EventSource.Name) is null)
        {
            return;
        }

        var level = eventData.Level switch
        {
            EventLevel.Critical or EventLevel.Error => LogLevel.Error,
            EventLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Debug,
        };

        if (!logger.IsEnabled(level))
        {
            return;
        }

        var message = eventData.Message is { Length: > 0 } template && eventData.Payload is { Count: > 0 }
            ? SafeFormat(template, eventData.Payload)
            : eventData.Message ?? eventData.EventName ?? "(no message)";

        logger.Log(level, "OpenTelemetry SDK [{EventSource}/{EventName}] {Message}",
            eventData.EventSource.Name, eventData.EventName, message);
    }

    private static string SafeFormat(string template, IReadOnlyCollection<object?> payload)
    {
        try
        {
            return string.Format(template, [.. payload]);
        }
        catch (FormatException)
        {
            return $"{template} | {string.Join(", ", payload)}";
        }
    }
}
