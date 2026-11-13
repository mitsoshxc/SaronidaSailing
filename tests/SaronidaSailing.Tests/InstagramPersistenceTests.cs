using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Tests;

public sealed class InstagramPersistenceTests : IAsyncLifetime
{
    private string _connectionString = "";
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    public Task DisposeAsync() => PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));

    [Fact]
    public async Task Migration_CreatesTheTablesAndTheSourceColumn()
    {
        await using var context = CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Contains(
            (await context.Database.GetAppliedMigrationsAsync()).ToList(),
            m => m.EndsWith("_AddInstagram", StringComparison.Ordinal));
        Assert.Empty(await context.Set<InstagramPost>().ToListAsync());
        Assert.Empty(await context.Set<IntegrationSetting>().ToListAsync());
        Assert.Empty(await context.Set<MediaAsset>().ToListAsync());
    }

    [Fact]
    public async Task ExistingMediaRows_BecomeUpload_ThroughTheColumnDefault()
    {
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "MediaAssets" ("Id","OriginalFileName","ContentHash","Slug","Widths","Width","Height","OriginalFileExtension","CreatedAt","UpdatedAt")
                VALUES (gen_random_uuid(),'old.jpg','legacy','legacy','480',480,320,'.jpg',now(),now())
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var context = CreateContext();
        var asset = await context.Set<MediaAsset>().SingleAsync();

        Assert.Equal(MediaSource.Upload, asset.Source);
    }

    [Fact]
    public async Task Source_RoundTrips()
    {
        var upload = Asset("up");
        var instagram = Asset("ig", MediaSource.Instagram);
        await SaveAsync(upload, instagram);

        await using var context = CreateContext();
        var rows = await context.Set<MediaAsset>().ToDictionaryAsync(x => x.Id);

        Assert.Equal(MediaSource.Upload, rows[upload.Id].Source);
        Assert.Equal(MediaSource.Instagram, rows[instagram.Id].Source);
    }

    [Fact]
    public async Task InstagramPost_RoundTripsEveryField_AndPostedAtIsUtc()
    {
        var asset = Asset("post");
        await SaveAsync(asset);
        var postedAt = new DateTime(2026, 11, 10, 12, 30, 0, DateTimeKind.Utc);
        var post = InstagramPost.Create("179", "https://www.instagram.com/p/abc/", "Caption", "CAROUSEL_ALBUM", asset.Id, postedAt);
        await using (var context = CreateContext())
        {
            new InstagramPostRepository(context).Add(post);
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var stored = await verify.Set<InstagramPost>().SingleAsync();

        Assert.Equal(post.Id, stored.Id);
        Assert.Equal("179", stored.InstagramId);
        Assert.Equal("https://www.instagram.com/p/abc/", stored.Permalink);
        Assert.Equal("Caption", stored.Caption);
        Assert.Equal("CAROUSEL_ALBUM", stored.MediaType);
        Assert.Equal(asset.Id, stored.MediaAssetId);
        Assert.Equal(postedAt, stored.PostedAt);
        Assert.Equal(DateTimeKind.Utc, stored.PostedAt.Kind);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, stored.CreatedAt);
    }

    [Fact]
    public async Task InstagramPost_DuplicateInstagramId_Throws()
    {
        var asset = Asset("dup");
        await SaveAsync(asset);
        await using var context = CreateContext();
        var repository = new InstagramPostRepository(context);
        repository.Add(Post("same", asset.Id));
        repository.Add(Post("same", asset.Id));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal("UX_InstagramPosts_InstagramId", Assert.IsType<PostgresException>(ex.InnerException).ConstraintName);
    }

    [Fact]
    public async Task DeletingAMediaAssetThatAPostUses_IsRestricted()
    {
        var asset = Asset("fk", MediaSource.Instagram);
        await SaveAsync(asset);
        await using (var context = CreateContext())
        {
            new InstagramPostRepository(context).Add(Post("p1", asset.Id));
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        verify.Set<MediaAsset>().Remove(await verify.Set<MediaAsset>().SingleAsync());

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());

        Assert.Equal(PostgresErrorCodes.RestrictViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task InstagramPost_UnknownMediaAsset_IsRejected()
    {
        await using var context = CreateContext();
        new InstagramPostRepository(context).Add(Post("p1", Guid.NewGuid()));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task PostRepository_ListsNewestFirst_LimitsAndListsAllTracked()
    {
        var asset = Asset("list", MediaSource.Instagram);
        await SaveAsync(asset);
        var older = Post("older", asset.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = Post("newest", asset.Id, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var middle = Post("middle", asset.Id, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await using (var context = CreateContext())
        {
            var writer = new InstagramPostRepository(context);
            writer.Add(older);
            writer.Add(newest);
            writer.Add(middle);
            await context.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var repository = new InstagramPostRepository(read);

        var top = await repository.ListNewestFirstAsync(2);
        Assert.Equal(["newest", "middle"], top.Select(x => x.InstagramId));
        Assert.Empty(read.ChangeTracker.Entries());

        var all = await repository.ListAllAsync();
        Assert.Equal(3, all.Count);
        Assert.Equal(3, read.ChangeTracker.Entries<InstagramPost>().Count());

        repository.Remove(all.Single(x => x.InstagramId == "older"));
        await read.SaveChangesAsync();
        await using var verify = CreateContext();
        Assert.Equal(2, await verify.Set<InstagramPost>().CountAsync());
    }

    [Fact]
    public async Task Settings_SetUpdateGetRemove()
    {
        await using (var context = CreateContext())
        {
            var repository = new IntegrationSettingRepository(context);
            Assert.Null(await repository.GetAsync("instagram.access-token"));

            await repository.SetAsync("instagram.access-token", "one");
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = new IntegrationSettingRepository(context);
            Assert.Equal("one", await repository.GetAsync("instagram.access-token"));

            _time.Advance(TimeSpan.FromMinutes(5));
            await repository.SetAsync("instagram.access-token", "two");
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = new IntegrationSettingRepository(context);
            Assert.Equal("two", await repository.GetAsync("instagram.access-token"));
            var row = await context.Set<IntegrationSetting>().SingleAsync();
            Assert.True(row.UpdatedAt > row.CreatedAt);

            await repository.RemoveAsync("instagram.access-token");
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = new IntegrationSettingRepository(context);
            Assert.Null(await repository.GetAsync("instagram.access-token"));
            await repository.RemoveAsync("instagram.access-token");
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Settings_SetTwiceInOneUnit_StoresOneRowWithTheLastValue()
    {
        await using (var context = CreateContext())
        {
            var repository = new IntegrationSettingRepository(context);
            await repository.SetAsync("k", "first");
            await repository.SetAsync("k", "last");
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var row = await verify.Set<IntegrationSetting>().SingleAsync();

        Assert.Equal("last", row.Value);
    }

    [Fact]
    public async Task Settings_DuplicateKeyRow_Throws()
    {
        await using var context = CreateContext();
        context.Set<IntegrationSetting>().Add(IntegrationSetting.Create("k", "a"));
        context.Set<IntegrationSetting>().Add(IntegrationSetting.Create("k", "b"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Library_AndExist_ExcludeInstagramAssets()
    {
        var upload = Asset("upload");
        var instagram = Asset("instagram", MediaSource.Instagram);
        await SaveAsync(upload, instagram);

        await using var context = CreateContext();
        var repository = new MediaAssetRepository(context);

        Assert.Equal([upload.Id], (await repository.ListNewestFirstAsync(0, 10)).Select(x => x.Id));
        Assert.Equal(new HashSet<Guid> { upload.Id }, await repository.ExistAsync([upload.Id, instagram.Id]));
        Assert.Empty(await repository.ExistAsync([instagram.Id]));
        Assert.Equal(instagram.Id, (await repository.GetByIdAsync(instagram.Id))?.Id);
        Assert.Single(await repository.GetByIdsAsync([instagram.Id]));
    }

    private async Task SaveAsync(params MediaAsset[] assets)
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

    private static MediaAsset Asset(string seed, MediaSource source = MediaSource.Upload) =>
        MediaAsset.Create(seed + ".jpg", seed.PadRight(64, '0'), seed, [480, 960], 1000, 800, ".jpg", source: source);

    private static InstagramPost Post(string instagramId, Guid mediaAssetId, DateTime? postedAt = null) =>
        InstagramPost.Create(
            instagramId,
            $"https://www.instagram.com/p/{instagramId}/",
            null,
            "IMAGE",
            mediaAssetId,
            postedAt ?? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
}
