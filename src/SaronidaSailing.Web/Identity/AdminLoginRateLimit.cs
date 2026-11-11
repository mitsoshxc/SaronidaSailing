using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Identity;

public static class AdminLoginRateLimit
{
    public const int PermitLimit = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public const string RetryAfterSeconds = "60";
    public const string RejectionMessage = "Too many attempts. Try again in a minute.";
    public const string AuditEvent = "login-rate-limited";

    public static readonly string RejectionPage = $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <meta name="robots" content="noindex, nofollow" />
            <title>Too many attempts — Saronida Sailing admin</title>
            <link rel="stylesheet" href="/css/site.css" />
        </head>
        <body class="admin">
            <main class="admin-main">
                <div class="admin-card">
                    <p class="admin-brand">Saronida Sailing admin</p>
                    <h1>Too many attempts</h1>
                    <p class="admin-error" role="alert">{RejectionMessage}</p>
                    <p><a href="/admin/login">Back to log in</a></p>
                </div>
            </main>
        </body>
        </html>
        """;

    private const string UnknownIp = "unknown";

    private static readonly string[] LimitedPaths =
    [
        "/admin/login",
        "/admin/login/code",
        "/admin/login/recovery",
        "/admin/two-factor/setup",
    ];

    public static bool IsLimited(string method, PathString path)
    {
        if (!HttpMethods.IsPost(method) || !path.HasValue)
        {
            return false;
        }

        var value = path.Value!.Length > 1 ? path.Value.TrimEnd('/') : path.Value;
        return LimitedPaths.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    public static RateLimitPartition<string> GetPartition(HttpContext context)
    {
        if (!IsLimited(context.Request.Method, context.Request.Path))
        {
            return RateLimitPartition.GetNoLimiter("not-limited");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? UnknownIp,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(GetPartition);
        options.OnRejected = async (rejected, cancellationToken) =>
        {
            var context = rejected.HttpContext;
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = RetryAfterSeconds;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.RequestServices.GetRequiredService<IAuditLog>()
                .Record(AuditEvent, null, context.Connection.RemoteIpAddress?.ToString(), context.Request.Path.Value);
            await context.Response.WriteAsync(RejectionPage, cancellationToken);
        };
    }
}
