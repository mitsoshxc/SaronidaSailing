using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SaronidaSailing.Infrastructure.Media;

namespace SaronidaSailing.Infrastructure.Health;

public sealed class MediaFolderHealthCheck(IOptions<MediaOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = Path.Combine(options.Value.RootPath, ".health-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllTextAsync(path, "ok", cancellationToken);
            File.Delete(path);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Media folder is not writable.", ex);
        }
    }
}
