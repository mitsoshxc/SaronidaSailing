using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SaronidaSailing.Infrastructure.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var isSqlite = db.Database.IsSqlite();

        if (isSqlite)
        {
            var connectionString = db.Database.GetConnectionString();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                DatabaseDirectory.EnsureExistsForFile(new SqliteConnectionStringBuilder(connectionString).DataSource);
            }
        }

        await db.Database.MigrateAsync();

        if (isSqlite)
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
    }
}
