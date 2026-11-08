using Microsoft.Extensions.DependencyInjection;

namespace SaronidaSailing.Infrastructure.Health;

public static class HealthCheckBuilderExtensions
{
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(this IHealthChecksBuilder builder) => builder
        .AddCheck<DatabaseHealthCheck>("database")
        .AddCheck<HangfireStorageHealthCheck>("hangfire")
        .AddCheck<MediaFolderHealthCheck>("media");
}
