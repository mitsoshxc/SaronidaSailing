using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Caching;

public sealed class PostgresCacheEvictionListener(
    string connectionString,
    ICacheTagEvictor evictor,
    ILogger<PostgresCacheEvictionListener> logger) : BackgroundService
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly string _listenerConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
    {
        Pooling = false,
        KeepAlive = 30,
        ApplicationName = CacheEvictionChannel.ListenerApplicationName
    }.ConnectionString;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = InitialBackoff;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(() => backoff = InitialBackoff, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache eviction listener lost its connection; retrying in {Delay}.", backoff);
            }

            try
            {
                await Task.Delay(backoff, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        }
    }

    private async Task ListenAsync(Action onListening, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_listenerConnectionString);
        await connection.OpenAsync(ct);

        var pending = new Queue<string>();
        connection.Notification += (_, e) =>
        {
            lock (pending)
            {
                pending.Enqueue(e.Payload);
            }
        };

        await using (var listen = new NpgsqlCommand($"LISTEN {CacheEvictionChannel.Name}", connection))
        {
            await listen.ExecuteNonQueryAsync(ct);
        }

        onListening();
        await evictor.EvictAllAsync(ct);

        while (true)
        {
            await connection.WaitAsync(ct);

            while (true)
            {
                string payload;
                lock (pending)
                {
                    if (!pending.TryDequeue(out payload!))
                    {
                        break;
                    }
                }

                await HandleAsync(payload, ct);
            }
        }
    }

    private async Task HandleAsync(string payload, CancellationToken ct)
    {
        string[]? tags = null;
        try
        {
            tags = JsonSerializer.Deserialize<string[]>(payload);
        }
        catch (JsonException)
        {
        }

        if (tags is null || tags.Any(t => t is null))
        {
            logger.LogWarning("Cache eviction notification was not a JSON string array; evicting everything.");
            await evictor.EvictAllAsync(ct);
            return;
        }

        await evictor.EvictTagsAsync(tags, ct);
    }
}
