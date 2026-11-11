using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class HangfireStorageTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public void Storage_IsSqliteAndAnswersStatistics()
    {
        using var _ = factory.CreateClient();

        var storage = factory.Services.GetRequiredService<JobStorage>();

        Assert.IsType<SQLiteStorage>(storage);
        Assert.NotNull(storage.GetMonitoringApi().GetStatistics());
    }

    [Fact]
    public void Startup_CreatesHangfireDatabaseSeparateFromContent()
    {
        using var _ = factory.CreateClient();

        Assert.True(File.Exists(factory.HangfirePath));
        Assert.NotEqual(factory.ContentConnectionString, factory.HangfirePath);
    }

    [Fact]
    public async Task BackgroundServer_Registers()
    {
        using var _ = factory.CreateClient();
        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (monitoring.Servers().Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.NotEmpty(monitoring.Servers());
    }
}
