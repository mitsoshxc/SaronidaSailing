using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Instagram;
using SaronidaSailing.Infrastructure.Media;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Tests;

public sealed class InstagramFeedServiceTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "saronida-ig-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));
    private readonly DataProtectionSecretProtector _protector = new(new EphemeralDataProtectionProvider());
    private readonly StubInstagramClient _client = new();
    private readonly RecordingPageCache _cache = new();
    private readonly FakeInstagramJobQueue _queue = new();
    private readonly List<AppDbContext> _contexts = [];
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        Directory.CreateDirectory(Path.Combine(_folder, "media"));
        Directory.CreateDirectory(Path.Combine(_folder, "originals"));
        _client.ExpiresAt = _time.GetUtcNow().UtcDateTime.AddDays(60);
        var connection = Connection(NewContext());
        Assert.True((await connection.CompleteConnectAsync("code", "https://example.test/cb", "admin@example.com", null)).Succeeded);
    }

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        await PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task FirstFetch_StoresPostsWithInstagramAssets_AndEvictsHome()
    {
        _client.AddPost("p1", "one", 1);
        _client.AddPost("p2", "two", 2);
        _client.AddPost("p3", null, 3);

        var result = await FetchAsync();

        Assert.Equal("ok:3", result.Code);
        var verify = NewContext();
        var stored = await verify.Set<InstagramPost>().OrderBy(x => x.InstagramId).ToListAsync();
        Assert.Equal(["p1", "p2", "p3"], stored.Select(x => x.InstagramId));
        Assert.Equal("one", stored[0].Caption);
        Assert.Null(stored[2].Caption);
        Assert.Equal("https://www.instagram.com/p/p1/", stored[0].Permalink);
        var assets = await verify.Set<MediaAsset>().ToListAsync();
        Assert.Equal(3, assets.Count);
        Assert.All(assets, a => Assert.Equal(MediaSource.Instagram, a.Source));
        Assert.Equal(assets.Select(a => a.Id).Order(), stored.Select(x => x.MediaAssetId).Order());
        Assert.Equal(["home"], Assert.Single(_cache.Evictions));
        await AssertLastFetchAsync("ok:3");
    }

    [Fact]
    public async Task SecondIdenticalFetch_DownloadsNothing_ChangesNothing_DoesNotEvict()
    {
        _client.AddPost("p1");
        _client.AddPost("p2");
        await FetchAsync();
        _client.Downloaded.Clear();
        _cache.Evictions.Clear();
        var before = await Snapshot();

        var result = await FetchAsync();

        Assert.Equal("ok:2", result.Code);
        Assert.Empty(_client.Downloaded);
        Assert.Empty(_cache.Evictions);
        Assert.Equal(before, await Snapshot());
        await AssertLastFetchAsync("ok:2");
    }

    [Fact]
    public async Task ChangedCaptionAndPermalink_AreUpdated_AndEvictHome()
    {
        _client.AddPost("p1", "old caption");
        await FetchAsync();
        _client.Downloaded.Clear();
        _cache.Evictions.Clear();
        _client.Replace("p1", x => x with { Caption = "new caption", Permalink = "https://www.instagram.com/reel/p1/", MediaType = "VIDEO" });

        var result = await FetchAsync();

        Assert.Equal("ok:1", result.Code);
        var post = await NewContext().Set<InstagramPost>().SingleAsync();
        Assert.Equal("new caption", post.Caption);
        Assert.Equal("https://www.instagram.com/reel/p1/", post.Permalink);
        Assert.Equal("IMAGE", post.MediaType);
        Assert.Empty(_client.Downloaded);
        Assert.Equal(["home"], Assert.Single(_cache.Evictions));
    }

    [Fact]
    public async Task PostGoneFromTheList_IsRemoved_AndItsAssetStays()
    {
        _client.AddPost("p1");
        _client.AddPost("p2");
        await FetchAsync();
        _cache.Evictions.Clear();
        _client.Items.RemoveAll(x => x.Id == "p2");

        var result = await FetchAsync();

        Assert.Equal("ok:1", result.Code);
        var verify = NewContext();
        Assert.Equal(["p1"], (await verify.Set<InstagramPost>().ToListAsync()).Select(x => x.InstagramId));
        Assert.Equal(2, await verify.Set<MediaAsset>().CountAsync());
        Assert.Equal(["home"], Assert.Single(_cache.Evictions));
    }

    [Fact]
    public async Task OneNewPostAndOneGone_KeepTheStoredCountRight()
    {
        _client.AddPost("p1");
        _client.AddPost("p2");
        _client.AddPost("p3");
        await FetchAsync();
        _client.Items.RemoveAll(x => x.Id == "p1");
        _client.AddPost("p4");

        var result = await FetchAsync();

        Assert.Equal("ok:3", result.Code);
        var verify = NewContext();
        Assert.Equal(["p2", "p3", "p4"], (await verify.Set<InstagramPost>().ToListAsync()).Select(x => x.InstagramId).Order());
        await AssertLastFetchAsync("ok:3");
    }

    [Fact]
    public async Task FailedDownload_SkipsThatPostOnly_AndTheNextRunStoresIt()
    {
        _client.AddPost("p1");
        var broken = _client.AddPost("p2");
        _client.AddPost("p3");
        _client.DownloadFailures[broken.ImageUrl] = new InstagramApiException(InstagramApiErrorKind.Transient, "timeout");

        var first = await FetchAsync();

        Assert.Equal("ok:2", first.Code);
        Assert.Equal(["p1", "p3"], (await NewContext().Set<InstagramPost>().ToListAsync()).Select(x => x.InstagramId).Order());

        _client.DownloadFailures.Clear();
        _client.Downloaded.Clear();

        var second = await FetchAsync();

        Assert.Equal("ok:3", second.Code);
        Assert.Equal([broken.ImageUrl], _client.Downloaded);
    }

    [Fact]
    public async Task RejectedImage_SkipsThatPostOnly()
    {
        _client.AddPost("p1");
        var bad = _client.AddPost("p2");
        _client.Images[bad.ImageUrl] = [1, 2, 3];

        var result = await FetchAsync();

        Assert.Equal("ok:1", result.Code);
        Assert.Equal(["p1"], (await NewContext().Set<InstagramPost>().ToListAsync()).Select(x => x.InstagramId));
    }

    [Fact]
    public async Task TokenInvalid_MarksTheConnectionInvalid_AndKeepsThePosts()
    {
        _client.AddPost("p1");
        await FetchAsync();
        _cache.Evictions.Clear();
        _client.MediaFailure = new InstagramApiException(InstagramApiErrorKind.TokenInvalid, "expired", 190);

        var result = await FetchAsync();

        Assert.Equal("failed:TokenInvalid", result.Code);
        var verify = NewContext();
        Assert.Single(await verify.Set<InstagramPost>().ToListAsync());
        Assert.Equal(InstagramConnectionState.Disconnected, (await Connection(verify).GetStatusAsync()).State);
        Assert.Empty(_cache.Evictions);
        await AssertLastFetchAsync("failed:TokenInvalid");
    }

    [Theory]
    [InlineData(InstagramApiErrorKind.Transient)]
    [InlineData(InstagramApiErrorKind.Other)]
    public async Task OtherApiFailures_KeepThePosts_AndTheConnection(InstagramApiErrorKind kind)
    {
        _client.AddPost("p1");
        await FetchAsync();
        _client.MediaFailure = new InstagramApiException(kind, "boom");

        var result = await FetchAsync();

        Assert.Equal($"failed:{kind}", result.Code);
        var verify = NewContext();
        Assert.Single(await verify.Set<InstagramPost>().ToListAsync());
        Assert.Equal(InstagramConnectionState.Connected, (await Connection(verify).GetStatusAsync()).State);
        await AssertLastFetchAsync($"failed:{kind}");
    }

    [Fact]
    public async Task ZeroItems_KeepThePosts_AndSkip()
    {
        _client.AddPost("p1");
        await FetchAsync();
        _cache.Evictions.Clear();
        _client.Items.Clear();

        var result = await FetchAsync();

        Assert.Equal("skipped:no-posts", result.Code);
        Assert.Single(await NewContext().Set<InstagramPost>().ToListAsync());
        Assert.Empty(_cache.Evictions);
        await AssertLastFetchAsync("skipped:no-posts");
    }

    [Fact]
    public async Task NotConnected_MakesNoApiCall_AndWritesTheStatus()
    {
        await Connection(NewContext()).DisconnectAsync("admin@example.com", null);

        var result = await FetchAsync();

        Assert.Equal("skipped:not-connected", result.Code);
        Assert.Equal(0, _client.MediaCalls);
        await AssertLastFetchAsync("skipped:not-connected");
    }

    [Fact]
    public async Task Activity_ReportsTheCountAndTheLastRun()
    {
        _client.AddPost("p1");
        _client.AddPost("p2");
        await FetchAsync();

        var activity = await Feed(NewContext()).GetActivityAsync();

        Assert.Equal(2, activity.PostCount);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, activity.LastFetchAt);
        Assert.Equal("ok:2", activity.LastFetchResult);
        Assert.Null(activity.LastRefreshAt);
        Assert.True(activity.HasAnything);
    }

    [Fact]
    public async Task GetFeed_IsNewestFirst_AndHonoursTake()
    {
        _client.AddPost("old", "old", 30);
        _client.AddPost("new", "new", 1);
        _client.AddPost("mid", "mid", 10);
        await FetchAsync();

        var all = await Feed(NewContext()).GetFeedAsync(12);
        var two = await Feed(NewContext()).GetFeedAsync(2);

        Assert.Equal(["new", "mid", "old"], all.Select(x => x.Caption));
        Assert.Equal(["new", "mid"], two.Select(x => x.Caption));
        Assert.Empty(await Feed(NewContext()).GetFeedAsync(0));
    }

    [Fact]
    public async Task GetFeed_WithNoPosts_IsEmpty() => Assert.Empty(await Feed(NewContext()).GetFeedAsync(6));

    [Fact]
    public async Task GetFeed_BuildsTheImageLikeContentImages()
    {
        _client.AddPost("p1");
        await FetchAsync();
        var context = NewContext();
        var asset = await context.Set<MediaAsset>().SingleAsync();

        var post = Assert.Single(await Feed(NewContext()).GetFeedAsync(6));

        Assert.Equal("https://www.instagram.com/p/p1/", post.Permalink);
        Assert.Equal(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), post.PostedAt);
        Assert.Equal(PageImage.From(asset).Sources, post.Image.Sources);
        Assert.Equal((asset.Id, asset.Width, asset.Height), (post.Image.Id, post.Image.Width, post.Image.Height));
        Assert.All(post.Image.Sources, s => Assert.StartsWith("/media/", s.Url));
        Assert.Equal(asset.GetWidths().Order(), post.Image.Sources.Select(s => s.Width));
    }

    [Fact]
    public async Task GetFeed_SkipsPostsWithoutUsableAssetOrHttpsPermalink_AndStillFillsTake()
    {
        _client.AddPost("good1", "good1", 1);
        _client.AddPost("insecure", "insecure", 2);
        _client.AddPost("relative", "relative", 3);
        _client.AddPost("good2", "good2", 4);
        await FetchAsync();
        await using (var context = NewContext())
        {
            var posts = await context.Set<InstagramPost>().ToListAsync();
            posts.Single(p => p.InstagramId == "insecure").Update("insecure", "http://www.instagram.com/p/insecure/");
            posts.Single(p => p.InstagramId == "relative").Update("relative", "/p/relative/");
            await context.SaveChangesAsync();
        }

        var feed = await Feed(NewContext()).GetFeedAsync(12);
        var two = await Feed(NewContext()).GetFeedAsync(2);

        Assert.Equal(["good1", "good2"], feed.Select(x => x.Caption));
        Assert.Equal(["good1", "good2"], two.Select(x => x.Caption));
    }

    [Fact]
    public async Task GetFeed_SkipsAPostWhoseAssetIsMissing()
    {
        _client.AddPost("kept", "kept", 1);
        _client.AddPost("lost", "lost", 2);
        await FetchAsync();
        var lostAssetId = (await NewContext().Set<InstagramPost>().SingleAsync(p => p.InstagramId == "lost")).MediaAssetId;

        var feed = await Feed(NewContext(), assetsWithout: lostAssetId).GetFeedAsync(12);

        Assert.Equal(["kept"], feed.Select(x => x.Caption));
    }

    private sealed class AssetRepositoryWithout(IMediaAssetRepository inner, Guid missing) : IMediaAssetRepository
    {
        public Task<MediaAsset?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

        public async Task<IReadOnlyList<MediaAsset>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            [.. (await inner.GetByIdsAsync(ids, ct)).Where(a => a.Id != missing)];

        public Task<MediaAsset?> GetByContentHashAsync(string contentHash, CancellationToken ct = default) =>
            inner.GetByContentHashAsync(contentHash, ct);

        public Task<IReadOnlyList<MediaAsset>> ListNewestFirstAsync(int skip, int take, CancellationToken ct = default) =>
            inner.ListNewestFirstAsync(skip, take, ct);

        public Task<IReadOnlySet<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            inner.ExistAsync(ids, ct);

        public void Add(MediaAsset asset) => inner.Add(asset);
    }

    private Task<InstagramFetchResult> FetchAsync() => Feed(NewContext()).FetchAsync();

    private InstagramFeedService Feed(AppDbContext context, Guid? assetsWithout = null)
    {
        var unitOfWork = new UnitOfWork(context);
        var options = Options.Create(new MediaOptions
        {
            RootPath = Path.Combine(_folder, "media"),
            OriginalsPath = Path.Combine(_folder, "originals"),
        });
        var media = new MediaService(new MediaAssetRepository(context), unitOfWork, options, new MediaProcessingGate());
        return new InstagramFeedService(
            _client, Connection(context), new InstagramPostRepository(context), assetsWithout is { } lost ? new AssetRepositoryWithout(new MediaAssetRepository(context), lost) : new MediaAssetRepository(context),
            new IntegrationSettingRepository(context), media, unitOfWork, _cache, NullLogger<InstagramFeedService>.Instance, _time);
    }

    private InstagramConnectionService Connection(AppDbContext context) =>
        new(_client, new IntegrationSettingRepository(context), new UnitOfWork(context), _protector, new InstagramAuditLog(),
            _queue, NullLogger<InstagramConnectionService>.Instance, _time);

    private AppDbContext NewContext()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options, _time);
        _contexts.Add(context);
        return context;
    }

    private async Task AssertLastFetchAsync(string expected)
    {
        var settings = new IntegrationSettingRepository(NewContext());
        Assert.Equal(expected, await settings.GetAsync(InstagramSettingKeys.LastFetchResult));
        Assert.Equal(
            _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            await settings.GetAsync(InstagramSettingKeys.LastFetchAt));
    }

    private async Task<string> Snapshot()
    {
        var verify = NewContext();
        var posts = await verify.Set<InstagramPost>().OrderBy(x => x.InstagramId)
            .Select(x => x.InstagramId + "|" + x.Caption + "|" + x.Permalink + "|" + x.MediaAssetId + "|" + x.UpdatedAt.Ticks)
            .ToListAsync();
        var assets = await verify.Set<MediaAsset>().CountAsync();
        return string.Join(";", posts) + "#" + assets;
    }
}
