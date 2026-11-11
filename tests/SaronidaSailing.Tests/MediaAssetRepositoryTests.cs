using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Tests;

public sealed class MediaAssetRepositoryTests : IAsyncLifetime
{
    private string _connectionString = "";
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    public Task DisposeAsync() => PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));

    [Fact]
    public async Task Add_ThenGetById_RoundTripsEveryField()
    {
        var asset = Asset("a");
        await using (var context = CreateContext())
        {
            new MediaAssetRepository(context).Add(asset);
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var stored = await new MediaAssetRepository(verify).GetByIdAsync(asset.Id);

        Assert.NotNull(stored);
        Assert.Equal(asset.OriginalFileName, stored.OriginalFileName);
        Assert.Equal(asset.ContentHash, stored.ContentHash);
        Assert.Equal(asset.Slug, stored.Slug);
        Assert.Equal("480,960,1600", stored.Widths);
        Assert.Equal([480, 960, 1600], stored.GetWidths());
        Assert.Equal(3000, stored.Width);
        Assert.Equal(2000, stored.Height);
        Assert.Equal(".jpg", stored.OriginalFileExtension);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
    }

    [Fact]
    public async Task GetById_Unknown_ReturnsNull()
    {
        await using var context = CreateContext();

        Assert.Null(await new MediaAssetRepository(context).GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByContentHash_FindsTheAsset()
    {
        var asset = Asset("b");
        await using (var context = CreateContext())
        {
            new MediaAssetRepository(context).Add(asset);
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var repository = new MediaAssetRepository(verify);

        Assert.Equal(asset.Id, (await repository.GetByContentHashAsync(asset.ContentHash))?.Id);
        Assert.Null(await repository.GetByContentHashAsync(Hash("zzz")));
    }

    [Fact]
    public async Task Add_DuplicateContentHash_Throws()
    {
        await using var context = CreateContext();
        var repository = new MediaAssetRepository(context);
        repository.Add(Asset("same"));
        repository.Add(Asset("same"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ListNewestFirst_OrdersByCreatedAtThenIdDescending_AndPages()
    {
        var oldest = Asset("oldest");
        var tieLow = Asset("tie-low", Guid.CreateVersion7(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var tieHigh = Asset("tie-high", Guid.CreateVersion7(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        var newest = Asset("newest");

        await SaveAtAsync(oldest);
        _time.Advance(TimeSpan.FromMinutes(1));
        await SaveAtAsync(tieLow, tieHigh);
        _time.Advance(TimeSpan.FromMinutes(1));
        await SaveAtAsync(newest);

        await using var context = CreateContext();
        var repository = new MediaAssetRepository(context);

        var all = await repository.ListNewestFirstAsync(0, 10);
        Assert.Equal([newest.Id, tieHigh.Id, tieLow.Id, oldest.Id], all.Select(x => x.Id));

        var secondPage = await repository.ListNewestFirstAsync(1, 2);
        Assert.Equal([tieHigh.Id, tieLow.Id], secondPage.Select(x => x.Id));

        Assert.Empty(await repository.ListNewestFirstAsync(4, 10));
    }

    [Fact]
    public async Task Exist_ReturnsOnlyTheIdsThatExist()
    {
        var first = Asset("e1");
        var second = Asset("e2");
        await SaveAtAsync(first, second);
        var missing = Guid.NewGuid();

        await using var context = CreateContext();
        var repository = new MediaAssetRepository(context);

        var found = await repository.ExistAsync([first.Id, missing, second.Id]);

        Assert.Equal(new HashSet<Guid> { first.Id, second.Id }, found);
        Assert.Empty(await repository.ExistAsync([]));
    }

    private async Task SaveAtAsync(params MediaAsset[] assets)
    {
        await using var context = CreateContext();
        var repository = new MediaAssetRepository(context);
        foreach (var asset in assets)
        {
            repository.Add(asset);
        }

        await context.SaveChangesAsync();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options, _time);

    private static string Hash(string seed) => seed.PadRight(64, '0');

    private static MediaAsset Asset(string seed, Guid? id = null) =>
        MediaAsset.Create(seed + ".jpg", Hash(seed), seed, [480, 960, 1600], 3000, 2000, ".jpg", id);
}
