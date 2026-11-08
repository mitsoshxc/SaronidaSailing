using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SaronidaSailing.Infrastructure.Health;
using SaronidaSailing.Infrastructure.Media;

namespace SaronidaSailing.Tests;

public sealed class MediaFolderHealthCheckTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "saronida-media-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WritableFolder_IsHealthyAndLeavesNoFileBehind()
    {
        Directory.CreateDirectory(_directory);

        var result = await CheckAsync(_directory);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public async Task MissingFolder_IsUnhealthyAndIsNotCreated()
    {
        var result = await CheckAsync(_directory);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.False(Directory.Exists(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Task<HealthCheckResult> CheckAsync(string rootPath)
    {
        var check = new MediaFolderHealthCheck(Options.Create(new MediaOptions { RootPath = rootPath }));
        return check.CheckHealthAsync(new HealthCheckContext());
    }
}
