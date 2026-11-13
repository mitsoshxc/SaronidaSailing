using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Web.Cli;

namespace SaronidaSailing.Tests;

public sealed class ProductionModeWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    protected override bool UseEmptyDatabase => true;

    protected override bool? MigrateAtStartup => false;
}

[Collection(WebHostCollection.Name)]
public class MigrateCommandTests
{
    private sealed record Run(int ExitCode, string Output, string Error);

    private static ServiceProvider BuildServices(string connectionString) => new ServiceCollection()
        .AddSingleton(TimeProvider.System)
        .AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString))
        .BuildServiceProvider();

    private static async Task<Run> RunAsync(IServiceProvider services, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await new MigrateCommand(services, output, error).RunAsync(args);
        return new Run(code, output.ToString(), error.ToString());
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string schema, string table)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table", connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    private static async Task<List<string>> AppliedMigrationsAsync(NpgsqlConnection connection)
    {
        var migrations = new List<string>();
        await using var command = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations;
    }

    [Fact]
    public async Task Migrate_OnEmptyDatabase_AppliesEfMigrationsAndHangfireSchema_ThenIsIdempotent()
    {
        var connectionString = await PostgresTestServer.CreateEmptyDatabaseAsync();
        try
        {
            await using var services = BuildServices(connectionString);

            var first = await RunAsync(services, "migrate");
            var second = await RunAsync(services, "migrate");

            Assert.Equal(0, first.ExitCode);
            Assert.Contains("_InitialCreate", first.Output);
            Assert.Contains("Hangfire schema up to date.", first.Output);
            Assert.Equal("", first.Error);

            Assert.Equal(0, second.ExitCode);
            Assert.Contains("No pending migrations.", second.Output);
            Assert.DoesNotContain("_InitialCreate", second.Output);
            Assert.Equal("", second.Error);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var migrations = await AppliedMigrationsAsync(connection);
            Assert.Single(migrations, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
            Assert.True(await TableExistsAsync(connection, "hangfire", "job"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(connectionString));
        }
    }

    [Fact]
    public async Task Migrate_WithExtraArgument_PrintsUsageAndReturns2()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        var run = await RunAsync(services, "migrate", "extra");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Usage:", run.Error);
        Assert.Equal("", run.Output);
    }

    [Fact]
    public async Task Migrate_WhenDatabaseIsUnreachable_PrintsErrorAndReturns1()
    {
        var unreachable = "Host=127.0.0.1;Port=1;Database=x;Username=x;Timeout=2;Command Timeout=2";
        await using var services = BuildServices(unreachable);

        var run = await RunAsync(services, "migrate");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Migration failed", run.Error);
        Assert.Equal("", run.Output);
    }

    [Fact]
    public void IsMigrateCommand_MatchesOnlyFirstArgument()
    {
        Assert.True(MigrateCommand.IsMigrateCommand(["migrate"]));
        Assert.True(MigrateCommand.IsMigrateCommand(["migrate", "extra"]));
        Assert.False(MigrateCommand.IsMigrateCommand([]));
        Assert.False(MigrateCommand.IsMigrateCommand(["admin", "migrate"]));
    }
}

[Collection(WebHostCollection.Name)]
public class StartupMigrationModeTests
{
    [Fact]
    public void Startup_WithoutMigrateAtStartup_FailsOnEmptyDatabaseAndNamesMigrateCommand()
    {
        using var factory = new ProductionModeWebApplicationFactory();

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        Assert.Contains(messages, m => m.Contains("migrate", StringComparison.Ordinal) && m.Contains("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Startup_WithoutMigrateAtStartup_OnPreparedDatabase_IsHealthyAndHangfireServerRegisters()
    {
        using var factory = new ProductionModeWebApplicationFactory();
        await using (var services = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddDbContext<AppDbContext>(options => options.UseNpgsql(factory.ContentConnectionString))
            .BuildServiceProvider())
        {
            await services.MigrateAsync();
        }

        await HomeContentTestData.EnsureGreekHomeContentAsync(factory.ContentConnectionString);
        NpgsqlConnection.ClearAllPools();

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());

        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (monitoring.Servers().Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.NotEmpty(monitoring.Servers());
    }
}
