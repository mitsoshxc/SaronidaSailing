using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Health;

public sealed class HomeContentHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Set<ContentItem>().AnyAsync(i => i.PageKey == "home" && i.Locale == "el", cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("No Greek home content.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Home content check failed.", ex);
        }
    }
}
