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

    private Task<InstagramFetchResult> FetchAsync() => Feed(NewContext()).FetchAsync();

    private InstagramFeedService Feed(AppDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var options = Options.Create(new MediaOptions
        {
            RootPath = Path.Combine(_folder, "media"),
            OriginalsPath = Path.Combine(_folder, "originals"),
        });
        var media = new MediaService(new MediaAssetRepository(context), unitOfWork, options, new MediaProcessingGate());
        return new InstagramFeedService(
            _client, Connection(context), new InstagramPostRepository(context), new IntegrationSettingRepository(context),
            media, unitOfWork, _cache, NullLogger<InstagramFeedService>.Instance, _time);
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
