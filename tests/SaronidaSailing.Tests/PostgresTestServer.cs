using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaronidaSailing.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SaronidaSailing.Tests;

public static class PostgresTestServer
{
    private const string Image = "postgres:18.6-alpine";
    private const string TemplateName = "saronida_template";

    private static readonly Lazy<Task<NpgsqlConnectionStringBuilder>> Server =
        new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    public static async Task<string> CreateDatabaseAsync() => await CreateAsync(TemplateName);

    public static async Task<string> CreateEmptyDatabaseAsync() => await CreateAsync(null);

    public static string DatabaseName(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Database
        ?? throw new ArgumentException("Connection string has no database.", nameof(connectionString));

    public static async Task DropDatabaseAsync(string name)
    {
        var server = await Server.Value;
        await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {Quote(name)} WITH (FORCE)");
    }

    private static async Task<NpgsqlConnectionStringBuilder> StartAsync()
    {
        var container = new PostgreSqlBuilder(Image)
            .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
            .Build();
        await container.StartAsync();

        var server = new NpgsqlConnectionStringBuilder(container.GetConnectionString());

        await ExecuteAsync(server, $"CREATE DATABASE {Quote(TemplateName)}");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(WithDatabase(server, TemplateName))
            .Options;
        await using (var context = new AppDbContext(options, TimeProvider.System))
        {
            await context.Database.MigrateAsync();
        }

        NpgsqlConnection.ClearAllPools();
        return server;
    }

    private static async Task<string> CreateAsync(string? template)
    {
        var server = await Server.Value;
        var name = $"saronida_test_{Guid.NewGuid():N}";
        var templateClause = template is null ? "" : $" TEMPLATE {Quote(template)}";

        await CreateLock.WaitAsync();
        try
        {
            await ExecuteAsync(server, $"CREATE DATABASE {Quote(name)}{templateClause}");
        }
        finally
        {
            CreateLock.Release();
        }

        return WithDatabase(server, name);
    }

    private static string WithDatabase(NpgsqlConnectionStringBuilder server, string database) =>
        new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = database }.ConnectionString;

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static async Task ExecuteAsync(NpgsqlConnectionStringBuilder server, string sql)
    {
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
