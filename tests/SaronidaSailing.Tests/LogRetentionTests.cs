using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class LogRetentionTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public void Files_OlderThan90Days_AreDeletedAndNewerKept()
    {
        Directory.CreateDirectory(factory.LogsPath);
        var old = CreateDatedFiles(factory.LogsPath, daysAgo: 91);
        var boundary = CreateDatedFiles(factory.LogsPath, daysAgo: 89);
        var recent = CreateDatedFiles(factory.LogsPath, daysAgo: 5);

        var services = factory.Services;
        services.GetRequiredService<IAuditLog>().Record("retention-check", "cli", null);
        services.GetRequiredService<ILoggerFactory>().CreateLogger("Saronida.Tests.Ordinary").LogWarning("retention-check");
        LogManager.Flush();

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.All(old, f => Assert.False(File.Exists(f), f));
        Assert.All(boundary.Concat(recent), f => Assert.True(File.Exists(f), f));
        Assert.True(File.Exists(Path.Combine(factory.LogsPath, $"audit-{today}.log")));
        Assert.True(File.Exists(Path.Combine(factory.LogsPath, $"app-{today}.log")));
    }

    private static string[] CreateDatedFiles(string folder, int daysAgo)
    {
        var date = DateTime.UtcNow.AddDays(-daysAgo);
        var paths = new[] { "audit", "app" }
            .Select(prefix => Path.Combine(folder, $"{prefix}-{date:yyyy-MM-dd}.log"))
            .ToArray();
        foreach (var path in paths)
        {
            File.WriteAllText(path, "old\n");
            File.SetCreationTimeUtc(path, date);
            File.SetLastWriteTimeUtc(path, date);
        }

        return paths;
    }
}
