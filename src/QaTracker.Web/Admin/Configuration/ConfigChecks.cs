using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QaTracker.Web.Auth;
using QaTracker.Web.Data;
using QaTracker.Web.Hosting.UpdateCheck;
using QaTracker.Web.Storage;

namespace QaTracker.Web.Admin.Configuration;

public enum CheckOutcome
{
    Ok,
    Warning,
    Failed,
    Skipped,
}

/// <summary>One live check's result. <see cref="Detail"/> never carries a host, URL or secret
/// (see <see cref="ConfigChecks.Safe"/>).</summary>
public sealed record CheckResult(string Name, CheckOutcome Outcome, string Detail);

/// <summary>
/// The "Run checks" button on System settings → Configuration: tests that the database, file
/// storage and single sign-on actually work right now, rather than only that they're configured.
/// Run on demand (the storage check writes, reads back and deletes a tiny file).
/// </summary>
public sealed partial class ConfigChecks(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IFileStorage storage,
    IConfiguration configuration,
    UpdateCheckService updateCheck,
    IServiceProvider services)
{
    public async Task<IReadOnlyList<CheckResult>> RunAsync(CancellationToken ct = default) =>
    [
        await DatabaseAsync(ct),
        await StorageAsync(ct),
        await SingleSignOnAsync(ct),
        Updates(),
    ];

    private async Task<CheckResult> DatabaseAsync(CancellationToken ct)
    {
        const string name = "Database";
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // EF reads a scalar query from a column named "Value".
            var version = await db.Database.SqlQueryRaw<string>("SELECT current_setting('server_version') AS \"Value\"").ToListAsync(ct);
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
            var server = $"PostgreSQL {version.FirstOrDefault()?.Split(' ')[0]}";
            return pending == 0
                ? new(name, CheckOutcome.Ok, $"Connected · {server} · schema up to date")
                : new(name, CheckOutcome.Warning, $"Connected · {server} · {pending} database change(s) not applied yet");
        }
        catch (Exception ex)
        {
            return new(name, CheckOutcome.Failed, $"Couldn't connect: {Safe(ex)}");
        }
    }

    private async Task<CheckResult> StorageAsync(CancellationToken ct)
    {
        const string name = "File storage";
        if (!storage.IsConfigured)
        {
            return new(name, CheckOutcome.Skipped, "Not configured, so uploads are off");
        }

        // Write, read back and delete a tiny file: catches wrong keys, a missing bucket or a region mismatch.
        var key = $"system-check/{Guid.NewGuid():N}.txt";
        var content = Encoding.UTF8.GetBytes($"QA Tracker storage check {DateTimeOffset.UtcNow:O}");
        try
        {
            await storage.PutAsync(key, new MemoryStream(content), "text/plain", ct);
            await using (var read = await storage.OpenReadAsync(key, ct))
            {
                using var copy = new MemoryStream();
                await read.CopyToAsync(copy, ct);
                if (!copy.ToArray().SequenceEqual(content))
                {
                    return new(name, CheckOutcome.Failed, "A test file came back different from what was written");
                }
            }

            await storage.DeleteAsync(key, ct);
            return new(name, CheckOutcome.Ok, "A test file was written, read back and deleted");
        }
        catch (Exception ex)
        {
            return new(name, CheckOutcome.Failed, $"The storage test failed: {Safe(ex)}");
        }
    }

    private async Task<CheckResult> SingleSignOnAsync(CancellationToken ct)
    {
        const string name = "Single sign-on";
        if (AuthConfig.ResolveProvider(configuration) == AuthProvider.None)
        {
            return new(name, CheckOutcome.Skipped, "Not configured (local accounts only)");
        }

        try
        {
            var options = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthConfig.OidcScheme);
            if (options.ConfigurationManager is null)
            {
                return new(name, CheckOutcome.Failed, "The sign-in provider isn't set up in this instance");
            }

            var metadata = await options.ConfigurationManager.GetConfigurationAsync(ct);
            return string.IsNullOrEmpty(metadata.AuthorizationEndpoint)
                ? new(name, CheckOutcome.Warning, "The provider's discovery document has no sign-in endpoint")
                : new(name, CheckOutcome.Ok, "The provider's discovery document loaded");
        }
        catch (Exception ex)
        {
            // The identity library's own message is long and full of addresses; say what to check instead.
            var status = StatusCode().Match(Chain(ex));
            return new(name, CheckOutcome.Failed,
                "Couldn't load the provider's discovery document" + (status.Success ? $" (HTTP {status.Groups[1].Value})" : "") +
                ". Check the tenant ID / authority settings, and that this server can reach the provider.");
        }
    }

    private CheckResult Updates()
    {
        const string name = "Update check";
        var status = updateCheck.Current;
        return status.State switch
        {
            UpdateState.Disabled => new(name, CheckOutcome.Skipped, "Switched off (QATRACKER_UPDATE_CHECK=false)"),
            UpdateState.NotChecked => new(name, CheckOutcome.Skipped, "Not checked yet"),
            UpdateState.UpToDate => new(name, CheckOutcome.Ok, "Up to date"),
            UpdateState.UpdateAvailable => new(name, CheckOutcome.Warning, $"Version {status.LatestVersion} is available"),
            _ => new(name, CheckOutcome.Warning, "The last check couldn't reach GitHub"),
        };
    }

    // URLs, host:port pairs (the host starting with a letter, so times like 12:30:00 aren't caught)
    // and "host=…" style fragments in an error message.
    [GeneratedRegex(@"[a-z][a-z0-9+.-]*://\S+|\b[a-z][\w.-]*:\d{2,5}\b|\b(host|server|endpoint|password|user id|username)\s*=\s*[^;\s]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex Addresses();

    [GeneratedRegex(@"StatusCode:\s*(\d{3})")]
    private static partial Regex StatusCode();

    private static string Chain(Exception ex)
    {
        var text = new StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            text.Append(e.Message).Append(' ');
        }
        return text.ToString();
    }

    /// <summary>An exception's message, with any address, endpoint or credential fragment blanked
    /// out and kept short — enough to diagnose (a wrong key, a region mismatch, a timeout) without
    /// showing where things are.</summary>
    public static string Safe(Exception ex)
    {
        var message = (ex.InnerException is { } inner && ex.Message.Length < 20 ? inner.Message : ex.Message).Trim();
        message = Addresses().Replace(message, "[address]");
        return message.Length > 180 ? message[..180].TrimEnd() + "…" : message;
    }
}
