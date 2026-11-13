using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Instagram;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Tests;

public sealed class InstagramConnectionServiceTests : IAsyncLifetime
{
    private const string Admin = "admin@example.com";
    private const string Redirect = "https://example.test/admin/instagram/callback";
    private const string LongToken = "LONG-LIVED-TOKEN-123";
    private const string Code = "AUTH-CODE-456";

    private string _connectionString = "";
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));
    private readonly DataProtectionSecretProtector _protector = new(new EphemeralDataProtectionProvider());
    private readonly RecordingAuditLog _audit = new();
    private readonly FakeClient _client = new();

    public async Task InitializeAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    public Task DisposeAsync() => PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));

    [Fact]
    public async Task Connect_StoresEveryKey_WithTheTokenProtected_AndAuditsOnce()
    {
        _client.ExpiresAt = _time.GetUtcNow().UtcDateTime.AddDays(60);
        await Run(s => s.SetAsync(InstagramSettingKeys.TokenInvalidAt, "2026-11-01T00:00:00.0000000Z"), save: true);

        var result = await Service(out _).CompleteConnectAsync(Code, Redirect, Admin, "10.0.0.1");

        Assert.True(result.Succeeded);
        Assert.Equal("saronida", result.Username);
        var rows = await Rows();
        Assert.Equal(
            [
                InstagramSettingKeys.AccessToken, InstagramSettingKeys.ConnectedAt, InstagramSettingKeys.ConnectedBy,
                InstagramSettingKeys.TokenExpiresAt, InstagramSettingKeys.UserId, InstagramSettingKeys.Username,
            ],
            rows.Keys.Order().ToArray());
        Assert.NotEqual(LongToken, rows[InstagramSettingKeys.AccessToken]);
        Assert.DoesNotContain(LongToken, rows[InstagramSettingKeys.AccessToken]);
        Assert.True(_protector.TryUnprotect(rows[InstagramSettingKeys.AccessToken], out var plain));
        Assert.Equal(LongToken, plain);
        Assert.Equal("2026-11-13T10:00:00.0000000Z", rows[InstagramSettingKeys.ConnectedAt]);
        Assert.Equal("2027-01-12T10:00:00.0000000Z", rows[InstagramSettingKeys.TokenExpiresAt]);
        Assert.Equal("17841400000000001", rows[InstagramSettingKeys.UserId]);
        Assert.Equal("saronida", rows[InstagramSettingKeys.Username]);
        Assert.Equal(Admin, rows[InstagramSettingKeys.ConnectedBy]);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("instagram-connected", Admin, "10.0.0.1", "username=@saronida"), entry);
        Assert.Equal(Code, _client.ReceivedCode);
        Assert.Equal(Redirect, _client.ReceivedRedirect);
    }

    [Theory]
    [InlineData(InstagramApiErrorKind.Other, null, "kind=Other")]
    [InlineData(InstagramApiErrorKind.TokenInvalid, 190, "kind=TokenInvalid code=190")]
    public async Task Connect_ApiFailure_StoresNothing_AndAuditsTheKindOnly(
        InstagramApiErrorKind kind, int? errorCode, string expectedDetails)
    {
        _client.Failure = new InstagramApiException(kind, $"boom {Code} {LongToken}", errorCode);

        var result = await Service(out _).CompleteConnectAsync(Code, Redirect, Admin, "10.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal(kind, result.ErrorKind);
        Assert.Empty(await Rows());
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("instagram-connect-failed", Admin, "10.0.0.1", expectedDetails), entry);
        Assert.DoesNotContain(Code, entry.Details);
        Assert.DoesNotContain(LongToken, entry.Details);
    }

    [Fact]
    public async Task Status_WithoutAToken_IsNotConnected_AndReportsCanConnect()
    {
        _client.IsConfigured = false;

        var status = await Service(out _).GetStatusAsync();

        Assert.Equal(InstagramConnectionState.NotConnected, status.State);
        Assert.False(status.CanConnect);
        Assert.Null(status.Username);
        Assert.Null(await Service(out _).GetAccessTokenAsync());
    }

    [Fact]
    public async Task Status_AfterConnect_IsConnected_AndGivesTheToken()
    {
        await Connect();

        var service = Service(out _);
        var status = await service.GetStatusAsync();

        Assert.Equal(InstagramConnectionState.Connected, status.State);
        Assert.True(status.CanConnect);
        Assert.Equal("saronida", status.Username);
        Assert.Equal(Admin, status.ConnectedBy);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, status.ConnectedAt);
        Assert.Equal(_time.GetUtcNow().UtcDateTime.AddDays(60), status.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, status.ExpiresAt!.Value.Kind);
        Assert.Equal(LongToken, await service.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Status_WithInvalidAtSet_IsDisconnected_AndHasNoToken()
    {
        await Connect();
        await Run(s => s.SetAsync(InstagramSettingKeys.TokenInvalidAt, "2026-11-13T11:00:00.0000000Z"), save: true);

        var service = Service(out _);

        Assert.Equal(InstagramConnectionState.Disconnected, (await service.GetStatusAsync()).State);
        Assert.Null(await service.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Status_WhenExpired_IsDisconnected_AndHasNoToken()
    {
        await Connect();
        _time.Advance(TimeSpan.FromDays(60));

        var service = Service(out _);

        Assert.Equal(InstagramConnectionState.Disconnected, (await service.GetStatusAsync()).State);
        Assert.Null(await service.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Status_WhenTheTokenCannotBeUnprotected_IsDisconnected_AndHasNoToken()
    {
        await Connect();
        await Run(s => s.SetAsync(InstagramSettingKeys.AccessToken, "not-a-protected-value"), save: true);

        var service = Service(out _);

        Assert.Equal(InstagramConnectionState.Disconnected, (await service.GetStatusAsync()).State);
        Assert.Null(await service.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Disconnect_RemovesEveryKey_KeepsPosts_AndAudits()
    {
        await Connect();
        await Run(s => s.SetAsync(InstagramSettingKeys.TokenInvalidAt, "2026-11-13T11:00:00.0000000Z"), save: true);
        var asset = MediaAsset.Create("ig.jpg", "ig".PadRight(64, '0'), "ig", [480], 1000, 800, ".jpg", source: MediaSource.Instagram);
        await using (var context = CreateContext())
        {
            new MediaAssetRepository(context).Add(asset);
            new InstagramPostRepository(context).Add(InstagramPost.Create(
                "p1", "https://www.instagram.com/p/p1/", null, "IMAGE", asset.Id, _time.GetUtcNow().UtcDateTime));
            await context.SaveChangesAsync();
        }

        _audit.Entries.Clear();
        var service = Service(out _);
        await service.DisconnectAsync(Admin, "10.0.0.2");

        Assert.Empty(await Rows());
        Assert.Equal(InstagramConnectionState.NotConnected, (await Service(out _).GetStatusAsync()).State);
        await using var verify = CreateContext();
        Assert.Single(await verify.Set<InstagramPost>().ToListAsync());
        Assert.Equal(("instagram-disconnected", Admin, "10.0.0.2", (string?)null), Assert.Single(_audit.Entries));
    }

    private async Task Connect()
    {
        _client.ExpiresAt = _time.GetUtcNow().UtcDateTime.AddDays(60);
        Assert.True((await Service(out _).CompleteConnectAsync(Code, Redirect, Admin, null)).Succeeded);
        _audit.Entries.Clear();
    }

    private InstagramConnectionService Service(out AppDbContext context)
    {
        context = CreateContext();
        return new InstagramConnectionService(
            _client, new IntegrationSettingRepository(context), new UnitOfWork(context), _protector, _audit, _time);
    }

    private async Task Run(Func<IIntegrationSettingRepository, Task> action, bool save)
    {
        await using var context = CreateContext();
        await action(new IntegrationSettingRepository(context));
        if (save)
        {
            await context.SaveChangesAsync();
        }
    }

    private async Task<Dictionary<string, string>> Rows()
    {
        await using var context = CreateContext();
        return await context.Set<IntegrationSetting>().ToDictionaryAsync(x => x.Key, x => x.Value);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options, _time);

    private sealed class FakeClient : IInstagramApiClient
    {
        public bool IsConfigured { get; set; } = true;

        public DateTime ExpiresAt { get; set; }

        public InstagramApiException? Failure { get; set; }

        public string? ReceivedCode { get; private set; }

        public string? ReceivedRedirect { get; private set; }

        public string BuildAuthorizeUrl(string redirectUri, string state) => throw new NotSupportedException();

        public Task<InstagramShortLivedToken> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
        {
            ReceivedCode = code;
            ReceivedRedirect = redirectUri;
            return Failure is null
                ? Task.FromResult(new InstagramShortLivedToken("SHORT-TOKEN", "17841400000000001"))
                : Task.FromException<InstagramShortLivedToken>(Failure);
        }

        public Task<InstagramLongLivedToken> ExchangeForLongLivedAsync(string shortLivedToken, CancellationToken ct = default) =>
            Task.FromResult(new InstagramLongLivedToken(LongToken, ExpiresAt));

        public Task<InstagramLongLivedToken> RefreshAsync(string longLivedToken, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<InstagramProfile> GetProfileAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(new InstagramProfile("17841400000000001", "saronida"));

        public Task<IReadOnlyList<InstagramMediaItem>> GetRecentMediaAsync(string token, int limit, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stream> DownloadImageAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<(string Event, string? User, string? Ip, string? Details)> Entries { get; } = [];

        public void Record(string eventName, string? userName, string? ipAddress, string? details = null) =>
            Entries.Add((eventName, userName, ipAddress, details));
    }
}
