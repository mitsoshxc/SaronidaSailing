using System.Globalization;
using Microsoft.Extensions.Options;
using NLog;

namespace SaronidaSailing.Web.Diagnostics;

public sealed class RequestDiagnosticsMiddleware(
    RequestDelegate next, RequestCounters counters, TimeProvider time, IOptions<DiagnosticsOptions> options)
{
    public const string LoggerName = "Diagnostics";

    private const int MaxLoggedPathLength = 256;

    private readonly Logger _logger = LogManager.GetLogger(LoggerName);

    public async Task InvokeAsync(HttpContext context)
    {
        counters.RequestStarted();
        var started = time.GetTimestamp();
        var threw = false;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            threw = !(ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested);
            throw;
        }
        finally
        {
            var elapsed = time.GetElapsedTime(started);
            var status = threw ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            counters.RequestCompleted(status);
            Report(context, status, elapsed);
        }
    }

    public static string CleanForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        var text = value.Length > MaxLoggedPathLength ? value[..MaxLoggedPathLength] : value;
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                var c = source[i];
                span[i] = c == '|' ? '/' : char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c;
            }
        });
    }

    private void Report(HttpContext context, int status, TimeSpan elapsed)
    {
        var request = context.Request;
        if (status == StatusCodes.Status429TooManyRequests
            && counters.TryTakeRateLimitedLine(time.GetUtcNow(), options.Value.RateLimitedLinesPerMinute))
        {
            _logger.Warn("{0}", string.Create(CultureInfo.InvariantCulture,
                $"Rate limited method={CleanForLog(request.Method)} path={CleanForLog(request.Path.Value)} ip={context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"));
        }

        if (elapsed.TotalMilliseconds > options.Value.SlowRequestMilliseconds)
        {
            _logger.Warn("{0}", string.Create(CultureInfo.InvariantCulture,
                $"Slow request method={CleanForLog(request.Method)} path={CleanForLog(request.Path.Value)} status={status} duration={(long)elapsed.TotalMilliseconds}ms"));
        }
    }
}
