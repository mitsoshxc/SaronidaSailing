using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SaronidaSailing.Infrastructure.Data;

public sealed record MigrationResult(IReadOnlyList<string> AppliedMigrations);

public static class DatabaseInitializer
{
    public const string HangfireSchema = "hangfire";

    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        if (services.GetRequiredService<DatabaseSettings>().MigrateAtStartup)
        {
            await services.MigrateAsync();
        }
        else
        {
            await services.EnsureUpToDateAsync();
        }
    }

    public static async Task<MigrationResult> MigrateAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        await db.Database.MigrateAsync();

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The content database has no connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        PostgreSqlObjectsInstaller.Install(connection, HangfireSchema);

        return new MigrationResult(pending);
    }

    public static async Task EnsureUpToDateAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"The database is behind: {pending.Count} pending migration(s): {string.Join(", ", pending)}. Run the 'migrate' command first.");
        }
    }
}
