using Microsoft.Data.Sqlite;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class DatabaseInitializerTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task Startup_AppliesMigrationsAndEnablesWal()
    {
        using var _ = factory.CreateClient();

        await using var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False");
        await connection.OpenAsync();

        await using var history = connection.CreateCommand();
        history.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
        var migrations = new List<string>();
        await using (var reader = await history.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                migrations.Add(reader.GetString(0));
            }
        }

        await using var journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode";
        var journalMode = (string?)await journal.ExecuteScalarAsync();

        Assert.Contains(migrations, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Equal("wal", journalMode);
    }
}
