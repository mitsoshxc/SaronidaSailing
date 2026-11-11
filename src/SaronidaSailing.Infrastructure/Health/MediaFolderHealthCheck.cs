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
            await ProbeAsync(options.Value.RootPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Media folder is not writable.", ex);
        }

        try
        {
            await ProbeAsync(options.Value.OriginalsPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Originals folder is not writable.", ex);
        }

        return HealthCheckResult.Healthy();
    }

    private static async Task ProbeAsync(string folder, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, ".health-" + Guid.NewGuid().ToString("N") + ".tmp");
        await File.WriteAllTextAsync(path, "ok", cancellationToken);
        File.Delete(path);
    }
}
