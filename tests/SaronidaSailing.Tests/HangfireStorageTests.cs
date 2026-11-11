using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class HangfireStorageTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public void Storage_IsPostgreSqlAndAnswersStatistics()
    {
        using var _ = factory.CreateClient();

        var storage = factory.Services.GetRequiredService<JobStorage>();

        Assert.IsType<PostgreSqlStorage>(storage);
        Assert.NotNull(storage.GetMonitoringApi().GetStatistics());
    }

    [Fact]
    public async Task Startup_CreatesHangfireSchemaInContentDatabase()
    {
        using var _ = factory.CreateClient();

        await using var connection = new NpgsqlConnection(factory.ContentConnectionString);
        await connection.OpenAsync();

        Assert.True(await TableExistsAsync(connection, "hangfire", "job"));
        Assert.True(await SchemaExistsAsync(connection, "hangfire"));
        Assert.True(await TableExistsAsync(connection, "public", "__EFMigrationsHistory"));
        Assert.False(await TableExistsAsync(connection, "hangfire", "__EFMigrationsHistory"));
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

    private static async Task<bool> SchemaExistsAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = @schema", connection);
        command.Parameters.AddWithValue("schema", schema);
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string schema, string table)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table", connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }
}
