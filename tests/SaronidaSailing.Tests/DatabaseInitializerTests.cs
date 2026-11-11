using Npgsql;

namespace SaronidaSailing.Tests;

public sealed class EmptyDatabaseWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    protected override bool UseEmptyDatabase => true;
}

[Collection(WebHostCollection.Name)]
public class DatabaseInitializerTests(EmptyDatabaseWebApplicationFactory factory) : IClassFixture<EmptyDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task Startup_AppliesMigrationsToEmptyDatabase()
    {
        using var _ = factory.CreateClient();

        await using var connection = new NpgsqlConnection(factory.ContentConnectionString);
        await connection.OpenAsync();

        await using var history = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"", connection);
        var migrations = new List<string>();
        await using (var reader = await history.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                migrations.Add(reader.GetString(0));
            }
        }

        Assert.Contains(migrations, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }
}
