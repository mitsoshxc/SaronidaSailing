using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SaronidaSailing.Infrastructure.Health;

public sealed class HangfireStorageHealthCheck(JobStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            storage.GetMonitoringApi().GetStatistics();
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire storage check failed.", ex));
        }
    }
}
