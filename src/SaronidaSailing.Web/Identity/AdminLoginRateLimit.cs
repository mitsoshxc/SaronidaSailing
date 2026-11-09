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
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.RequestServices.GetRequiredService<IAuditLog>()
                .Record(AuditEvent, null, context.Connection.RemoteIpAddress?.ToString(), context.Request.Path.Value);
            await context.Response.WriteAsync(RejectionMessage, cancellationToken);
        };
    }
}
