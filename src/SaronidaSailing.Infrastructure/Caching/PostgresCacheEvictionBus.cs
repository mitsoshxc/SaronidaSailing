using System.Text.Json;
using Npgsql;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Caching;

public sealed class PostgresCacheEvictionBus(string connectionString) : ICacheEvictionBus
{
    public async Task PublishAsync(IReadOnlyCollection<string> tags, CancellationToken ct = default)
    {
        if (tags.Count == 0)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection);
        command.Parameters.AddWithValue("channel", CacheEvictionChannel.Name);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(tags));
        await command.ExecuteNonQueryAsync(ct);
    }
}
