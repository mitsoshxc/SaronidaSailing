using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Tests;

public sealed class BaseEntityTimestampTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));

    public BaseEntityTimestampTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Added_SetsCreatedAtAndUpdatedAtToNow()
    {
        var entity = new TestEntity { Name = "a" };

        await using (var context = CreateContext())
        {
            context.Add(entity);
            await context.SaveChangesAsync();
        }

        Assert.Equal(_time.GetUtcNow().UtcDateTime, entity.CreatedAt);
        Assert.Equal(entity.CreatedAt, entity.UpdatedAt);
    }

    [Fact]
    public async Task Modified_UpdatesOnlyUpdatedAt()
    {
        var created = _time.GetUtcNow().UtcDateTime;
        var entity = new TestEntity { Name = "a" };
        await using (var context = CreateContext())
        {
            context.Add(entity);
            await context.SaveChangesAsync();
        }

        _time.Advance(TimeSpan.FromHours(2));

        await using (var context = CreateContext())
        {
            var loaded = await context.Set<TestEntity>().SingleAsync();
            loaded.Name = "b";
            await context.SaveChangesAsync();
            entity = loaded;
        }

        Assert.Equal(created, entity.CreatedAt);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, entity.UpdatedAt);
        Assert.True(entity.UpdatedAt > entity.CreatedAt);
    }

    [Fact]
    public async Task ReadBack_ReturnsUtcKind()
    {
        await using (var context = CreateContext())
        {
            context.Add(new TestEntity { Name = "a" });
            await context.SaveChangesAsync();
        }

        await using var fresh = CreateContext();
        var loaded = await fresh.Set<TestEntity>().SingleAsync();

        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, loaded.UpdatedAt.Kind);
    }

    public void Dispose() => _connection.Dispose();

    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options, _time);

    private sealed class TestDbContext(DbContextOptions options, TimeProvider timeProvider)
        : AppDbContext(options, timeProvider)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TestEntity>();
        }
    }

    private sealed class TestEntity : BaseEntity
    {
        public string Name { get; set; } = "";
    }
}
