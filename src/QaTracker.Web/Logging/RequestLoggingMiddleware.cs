using System.Diagnostics;

namespace QaTracker.Web.Logging;

/// <summary>
/// Writes a single summary line per HTTP request: method, path, response status and
/// elapsed milliseconds. Unhandled exceptions are logged at Error (with the exception)
/// and rethrown so the normal error pipeline still runs.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var request = context.Request;
        var path = $"{request.Path}{request.QueryString}";

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var failedMs = (int)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            logger.LogError(ex, "{Method} {Path} failed after {ElapsedMs}ms", request.Method, path, failedMs);
            throw;
        }

        var elapsedMs = (int)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        logger.LogInformation("{Method} {Path} {StatusCode} {ElapsedMs}ms",
            request.Method, path, context.Response.StatusCode, elapsedMs);
    }
}

public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();
}
