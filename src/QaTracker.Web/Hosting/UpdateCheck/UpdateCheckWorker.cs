namespace QaTracker.Web.Hosting.UpdateCheck;

/// <summary>
/// Background poller for <see cref="UpdateCheckService"/>. Unauthenticated GitHub API calls
/// are limited to 60/hour per IP, so this checks rarely: roughly every <see cref="Interval"/>
/// (with jitter, so several replicas behind one IP don't fire in lockstep), and retries a
/// failed check after <see cref="RetryInterval"/> instead of waiting the full interval.
/// The manual "Check for updates" button on /admin still works alongside it.
/// </summary>
public sealed class UpdateCheckWorker(
    UpdateCheckService updateCheck,
    BuildInfo buildInfo,
    ILogger<UpdateCheckWorker> logger) : BackgroundService
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    /// <summary>Random spread applied to each wait, as a fraction (±10%).</summary>
    private const double Jitter = 0.10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!buildInfo.UpdateCheckEnabled)
        {
            return;
        }

        var delay = Jittered(InitialDelay);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var status = await updateCheck.CheckAsync(stoppingToken);
            logger.LogDebug("Scheduled update check finished: {State}.", status.State);

            delay = Jittered(status.State == UpdateState.CheckFailed ? RetryInterval : Interval);
        }
    }

    private static TimeSpan Jittered(TimeSpan baseDelay) =>
        baseDelay * (1 + (Random.Shared.NextDouble() * 2 - 1) * Jitter);
}
