using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Tests;

public sealed class ContentHistorySaveIdMigrationTests : IDisposable
{
    private const string PreviousMigration = "20261109113901_AddContent";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ContentHistorySaveIdMigrationTests() => _connection.Open();

    [Fact]
    public void ExistingHistoryRows_GetTheirOwnIdAsSaveId()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero)));
        var migrator = context.GetService<IMigrator>();
        migrator.Migrate(PreviousMigration);

        var itemId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        Execute(
            "INSERT INTO ContentItems (Id, PageKey, SectionKey, FieldKey, Locale, ListItemId, Type, Value, UpdatedBy, CreatedAt, UpdatedAt) "
            + "VALUES ($id, 'home', 'hero', 'title', 'el', NULL, 'Text', 'b', 'a@example.com', '2026-03-01', '2026-03-01')",
            ("$id", itemId));
        foreach (var (id, value) in new[] { (firstId, "a"), (secondId, "b") })
        {
            Execute(
                "INSERT INTO ContentHistory (Id, ContentItemId, Value, ChangedBy, CreatedAt, UpdatedAt) "
                + "VALUES ($id, $item, $value, 'a@example.com', '2026-03-01', '2026-03-01')",
                ("$id", id), ("$item", itemId), ("$value", value));
        }

        migrator.Migrate();

        using var read = _connection.CreateCommand();
        read.CommandText = "SELECT Id, SaveId FROM ContentHistory";
        using var reader = read.ExecuteReader();
        var rows = new List<(string Id, string SaveId)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(r.Id, r.SaveId));
        Assert.Equal(
            new[] { firstId.ToString().ToUpperInvariant(), secondId.ToString().ToUpperInvariant() }.Order(),
            rows.Select(r => r.SaveId).Order());
    }

    public void Dispose() => _connection.Dispose();

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
