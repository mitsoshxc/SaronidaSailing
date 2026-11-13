using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Content;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Tests;

internal sealed class FakeSeedSource(params ContentSeedGroup[] groups) : IContentSeedSource
{
    public IReadOnlyList<ContentSeedGroup> GetGroups() => groups;
}

internal sealed class RecordingLock : IDistributedLock
{
    public List<(string Resource, TimeSpan Timeout)> Acquired { get; } = [];

    public int Released { get; private set; }

    public Exception? Failure { get; set; }

    public Task<IAsyncDisposable> AcquireAsync(string resource, TimeSpan timeout, CancellationToken ct = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<IAsyncDisposable>(Failure);
        }

        Acquired.Add((resource, timeout));
        return Task.FromResult<IAsyncDisposable>(new Lease(this));
    }

    private sealed class Lease(RecordingLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Released++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

internal sealed class NoOpAuditLog : IAuditLog
{
    public void Record(string eventName, string? userName, string? ipAddress, string? details = null)
    {
    }
}

internal sealed class NoOpInvalidator : IPageCacheInvalidator
{
    public List<string[]> Calls { get; } = [];

    public Task EvictPagesAsync(IReadOnlyCollection<string> pageKeys, CancellationToken ct = default)
    {
        Calls.Add([.. pageKeys]);
        return Task.CompletedTask;
    }
}

internal sealed class ContentSeedTestDatabase(string connectionString)
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));

    public NoOpInvalidator Invalidator { get; } = new();

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options, _time);

    public ContentService CreateService(AppDbContext context) =>
        new(
            new ContentItemRepository(context),
            new ContentListItemRepository(context),
            new ContentSectionRepository(context),
            new ContentHistoryRepository(context),
            new MediaAssetRepository(context),
            new UnitOfWork(context),
            new RichTextSanitizer(),
            Invalidator,
            new NoOpAuditLog(),
            [],
            NullLogger<ContentService>.Instance);

    public async Task RunSeederAsync(
        IContentSeedSource source, IDistributedLock? distributedLock = null, ILogger<ContentSeeder>? logger = null)
    {
        await using var context = CreateContext();
        var seeder = new ContentSeeder(
            source,
            CreateService(context),
            new ContentItemRepository(context),
            new ContentListItemRepository(context),
            new IntegrationSettingRepository(context),
            new UnitOfWork(context),
            distributedLock ?? new RecordingLock(),
            logger ?? NullLogger<ContentSeeder>.Instance);
        await seeder.SeedAsync();
    }

    public async Task ApplyAsync(params ContentChange[] changes)
    {
        await using var context = CreateContext();
        var result = await CreateService(context).ApplyChangesAsync(changes, new ContentEditor("admin@example.com", null));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    public async Task<List<ContentItem>> ItemsAsync()
    {
        await using var context = CreateContext();
        return await context.Set<ContentItem>().AsNoTracking().ToListAsync();
    }

    public async Task<int> HistoryCountAsync()
    {
        await using var context = CreateContext();
        return await context.Set<ContentHistory>().CountAsync();
    }

    public async Task<List<ContentListItem>> ListItemsAsync()
    {
        await using var context = CreateContext();
        return await context.Set<ContentListItem>().AsNoTracking().OrderBy(l => l.SortOrder).ToListAsync();
    }

    public async Task<List<IntegrationSetting>> MarkersAsync()
    {
        await using var context = CreateContext();
        return await context.Set<IntegrationSetting>().AsNoTracking()
            .Where(s => s.Key.StartsWith("seed.")).ToListAsync();
    }
}

internal static class SeedCounts
{
    public static int ValueCount(IEnumerable<ContentSeedGroup> groups) =>
        groups.Sum(g => g.Fields.Concat(g.Lists.SelectMany(l => l.Items).SelectMany(i => i.Fields)).Sum(f => f.Values.Count));
}
