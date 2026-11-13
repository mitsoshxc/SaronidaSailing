using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Jobs;

namespace SaronidaSailing.Tests;

public sealed class SeededWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    protected override bool SeedAtStartup => true;

    protected override bool? MigrateAtStartup => MigrateOnStart;

    public bool MigrateOnStart { get; init; } = true;
}

[Collection(WebHostCollection.Name)]
public class ContentSeedFooterTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static string Footer(string html)
    {
        var start = html.IndexOf("<footer class=\"site-footer\">", StringComparison.Ordinal);
        var end = html.IndexOf("</footer>", start, StringComparison.Ordinal);
        return html[start..(end + "</footer>".Length)];
    }

    [Fact]
    public async Task Footer_Greek_ShowsTheSeededContent()
    {
        var footer = Footer(await factory.CreateClient().GetStringAsync("/"));

        Assert.Contains("Ιδιωτικές εκδρομές με καταμαράν Lagoon 500 από την Ανάβυσσο.", footer);
        Assert.Matches(@"href=""tel:(\+|&#x2B;)306940057799""", footer);
        Assert.Matches(@"href=""tel:(\+|&#x2B;)306978856418""", footer);
        Assert.Contains("Άγιος Νικόλαος, Ανάβυσσος", footer);
        Assert.Contains("href=\"https://www.instagram.com/saronidasailing/\"", footer);
        Assert.Contains("href=\"https://www.facebook.com/Saronida-Sailing-108336665074413/\"", footer);
        Assert.Contains("Η Saronida Sailing στο Instagram", footer);
    }

    [Fact]
    public async Task Footer_English_ShowsTheDraftedEnglish()
    {
        var footer = Footer(await factory.CreateClient().GetStringAsync("/en/"));

        Assert.Contains("Private catamaran trips on a Lagoon 500 from Anavyssos.", footer);
        Assert.Matches(@"href=""tel:(\+|&#x2B;)306940057799""", footer);
        Assert.Matches(@"href=""tel:(\+|&#x2B;)306978856418""", footer);
        Assert.Contains("Saronida Sailing on Instagram", footer);
        Assert.Contains("Saronida Sailing on Facebook", footer);
        Assert.Contains("href=\"https://www.instagram.com/saronidasailing/\"", footer);
        Assert.Contains("href=\"https://www.facebook.com/Saronida-Sailing-108336665074413/\"", footer);
    }
}

[Collection(WebHostCollection.Name)]
public class ContentSeedTwoPodTests
{
    [Fact]
    public async Task TwoHostsStartingTogether_SeedEachValueOnce()
    {
        using var hostA = new SeededWebApplicationFactory { MigrateOnStart = false };
        using var hostB = new SeededWebApplicationFactory { MigrateOnStart = false };
        hostB.UseDatabaseOf(hostA);

        await using (var install = new NpgsqlConnection(hostA.ContentConnectionString))
        {
            await install.OpenAsync();
            PostgreSqlObjectsInstaller.Install(install, "hangfire");
        }

        await Task.WhenAll(Task.Run(() => hostA.Services), Task.Run(() => hostB.Services));

        var groups = new SaronidaSailing.Web.Content.EmbeddedContentSeedSource().GetGroups();
        await using var connection = new NpgsqlConnection(hostA.ContentConnectionString);
        await connection.OpenAsync();
        Assert.Equal(groups.SelectMany(g => g.Fields).Sum(f => f.Values.Count), await Count(connection, "\"ContentItems\""));
        Assert.Equal(0, await Scalar(
            connection,
            "SELECT COUNT(*) FROM (SELECT \"SectionKey\", \"FieldKey\", \"Locale\" FROM \"ContentItems\" "
            + "GROUP BY \"SectionKey\", \"FieldKey\", \"Locale\" HAVING COUNT(*) > 1) d"));
        Assert.Equal(groups.Count, await Count(connection, "\"IntegrationSettings\" WHERE \"Key\" LIKE 'seed.%'"));
    }

    private static Task<long> Count(NpgsqlConnection connection, string from) =>
        Scalar(connection, $"SELECT COUNT(*) FROM {from}");

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

[Collection(WebHostCollection.Name)]
public class HangfireDistributedLockTests(TempDatabaseWebApplicationFactory factory)
    : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private HangfireDistributedLock CreateLock()
    {
        using var _ = factory.CreateClient();
        return new HangfireDistributedLock(factory.Services.GetRequiredService<JobStorage>());
    }

    [Fact]
    public async Task SecondAcquire_WaitsForTheFirstToBeReleased()
    {
        var distributedLock = CreateLock();
        var resource = "test:" + Guid.NewGuid().ToString("N");
        var first = await distributedLock.AcquireAsync(resource, TimeSpan.FromSeconds(5));

        var second = distributedLock.AcquireAsync(resource, TimeSpan.FromSeconds(20));
        await Task.Delay(500);
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();
        await using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Acquire_WhenHeldPastTheTimeout_Throws()
    {
        var distributedLock = CreateLock();
        var resource = "test:" + Guid.NewGuid().ToString("N");
        await using var first = await distributedLock.AcquireAsync(resource, TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<Exception>(() => distributedLock.AcquireAsync(resource, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Seeder_UsesTheRealLock_AndSeedsOnce()
    {
        var distributedLock = CreateLock();
        var db = new ContentSeedTestDatabase(factory.ContentConnectionString);
        var source = new FakeSeedSource(new ContentSeedGroup(
            "lock-test", 1,
            [new SeedField("lock-test.a.b", SaronidaSailing.Core.Entities.ContentType.Text, new Dictionary<string, string> { ["el"] = "x" })],
            []));

        await Task.WhenAll(db.RunSeederAsync(source, distributedLock), db.RunSeederAsync(source, distributedLock));

        Assert.Single(await db.ItemsAsync(), i => i.PageKey == "lock-test");
        Assert.Single(await db.MarkersAsync(), m => m.Key == "seed.lock-test");
    }
}
