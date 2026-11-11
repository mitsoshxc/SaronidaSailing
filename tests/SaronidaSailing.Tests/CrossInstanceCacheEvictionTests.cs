using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public sealed class CrossInstanceCacheEvictionTests : IClassFixture<TempDatabaseWebApplicationFactory>, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TempDatabaseWebApplicationFactory _hostA;
    private readonly TempDatabaseWebApplicationFactory _hostB = new();
    private readonly HttpClient _clientB;

    public CrossInstanceCacheEvictionTests(TempDatabaseWebApplicationFactory hostA)
    {
        _hostA = hostA;
        _hostB.UseDatabaseOf(hostA);
        _clientB = AdminTestHelpers.NewClient(_hostB);
    }

    public void Dispose() => _hostB.Dispose();

    private static string NewValue() => "Title " + Guid.NewGuid().ToString("N");

    private async Task SaveThroughHostA(string value)
    {
        using var scope = _hostA.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IContentService>().ApplyChangesAsync(
            [new SetField("home.seo.title", "el", ContentType.Text, value)], new ContentEditor("test@example.com", null));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private async Task SaveSilently(string value)
    {
        using var scope = _hostA.Services.CreateScope();
        var item = await scope.ServiceProvider.GetRequiredService<IContentItemRepository>()
            .FindStandaloneAsync("home", "seo", "title", "el");
        Assert.NotNull(item);
        item.SetValue(value, "silent@example.com");
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private Task<string> GetFromHostB() => _clientB.GetStringAsync("/");

    private async Task PrimeHostB(string value)
    {
        await SaveThroughHostA(value);
        await WaitForTitle(value);
    }

    private async Task WaitForTitle(string value)
    {
        var expected = $"<title>{value}</title>";
        var deadline = DateTime.UtcNow + Timeout;
        string html;
        do
        {
            html = await GetFromHostB();
            if (html.Contains(expected))
            {
                return;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Contains(expected, html);
    }

    private async Task WaitForListeners()
    {
        await GetFromHostB();
        _ = _hostA.Services;

        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var listening = await ScalarAsync(
                "SELECT count(*) FROM pg_stat_activity WHERE application_name = 'saronida-cache-listener' " +
                "AND datname = current_database() AND query LIKE 'LISTEN%'");
            if (listening >= 2)
            {
                await Task.Delay(300);
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("Both cache listeners did not start listening.");
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_hostA.ContentConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_hostA.ContentConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SaveOnHostA_EvictsHostBCache()
    {
        await WaitForListeners();
        var first = NewValue();
        await PrimeHostB(first);

        await SaveSilently(NewValue());
        Assert.Contains($"<title>{first}</title>", await GetFromHostB());

        var second = NewValue();
        await SaveThroughHostA(second);

        await WaitForTitle(second);
    }

    [Fact]
    public async Task ListenerReconnect_EvictsEverything()
    {
        await WaitForListeners();
        var first = NewValue();
        await PrimeHostB(first);

        var second = NewValue();
        await SaveSilently(second);
        Assert.Contains($"<title>{first}</title>", await GetFromHostB());

        await ExecuteAsync(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
            "WHERE application_name = 'saronida-cache-listener' AND datname = current_database()");

        await WaitForTitle(second);
    }

    [Fact]
    public async Task MalformedPayload_EvictsEverything()
    {
        await WaitForListeners();
        var first = NewValue();
        await PrimeHostB(first);

        var second = NewValue();
        await SaveSilently(second);
        Assert.Contains($"<title>{first}</title>", await GetFromHostB());

        await ExecuteAsync("SELECT pg_notify('saronida_cache_evict', 'not json')");

        await WaitForTitle(second);
    }
}
