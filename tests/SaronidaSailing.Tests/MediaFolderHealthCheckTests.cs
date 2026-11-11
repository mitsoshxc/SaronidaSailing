using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SaronidaSailing.Infrastructure.Health;
using SaronidaSailing.Infrastructure.Media;

namespace SaronidaSailing.Tests;

public sealed class MediaFolderHealthCheckTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "saronida-media-" + Guid.NewGuid().ToString("N"));

    private string Media => Path.Combine(_directory, "media");

    private string Originals => Path.Combine(_directory, "originals");

    [Fact]
    public async Task WritableFolders_AreHealthyAndLeaveNoFileBehind()
    {
        Directory.CreateDirectory(Media);
        Directory.CreateDirectory(Originals);

        var result = await CheckAsync(Media, Originals);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Empty(Directory.GetFileSystemEntries(Media));
        Assert.Empty(Directory.GetFileSystemEntries(Originals));
    }

    [Fact]
    public async Task MissingMediaFolder_IsUnhealthyAndIsNotCreated()
    {
        Directory.CreateDirectory(Originals);

        var result = await CheckAsync(Media, Originals);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Media folder is not writable.", result.Description);
        Assert.False(Directory.Exists(Media));
    }

    [Fact]
    public async Task MissingOriginalsFolder_IsUnhealthyAndIsNotCreated()
    {
        Directory.CreateDirectory(Media);

        var result = await CheckAsync(Media, Originals);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Originals folder is not writable.", result.Description);
        Assert.False(Directory.Exists(Originals));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Task<HealthCheckResult> CheckAsync(string rootPath, string originalsPath)
    {
        var check = new MediaFolderHealthCheck(Options.Create(new MediaOptions { RootPath = rootPath, OriginalsPath = originalsPath }));
        return check.CheckHealthAsync(new HealthCheckContext());
    }
}
