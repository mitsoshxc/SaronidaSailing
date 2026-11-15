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

public sealed class InstagramAppSettingsServiceTests : IAsyncLifetime
{
    private const string Admin = "admin@example.com";
    private const string AppId = "1234567890";
    private const string Secret = "APP-SECRET-0123456789";

    private string _connectionString = "";
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly DataProtectionSecretProtector _protector = new(new EphemeralDataProtectionProvider());
    private readonly InstagramAuditLog _audit = new();

    public async Task InitializeAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    public Task DisposeAsync() => PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));

    [Fact]
    public async Task Status_WhenNothingIsSaved_IsNotConfigured()
    {
        var status = await Service().GetStatusAsync();

        Assert.Equal(new InstagramAppStatus(null, false, false, false), status);
        Assert.Null(await Service().GetCredentialsAsync());
    }

    [Fact]
    public async Task FirstSave_SavesBoth_AndAuditsWithoutTheValues()
    {
        var result = await Service().SaveAsync($"  {AppId} ", $" {Secret} ", false, Admin, "10.0.0.1");

        Assert.True(result.Succeeded);
        Assert.False(result.Disconnected);
        Assert.Equal(new InstagramAppStatus(AppId, true, false, true), await Service().GetStatusAsync());
        Assert.Equal(new InstagramAppCredentials(AppId, Secret), await Service().GetCredentialsAsync());
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("instagram-app-saved", Admin, "10.0.0.1", "app-id-changed=true secret-replaced=true disconnected=false"), entry);
    }

    [Theory]
    [InlineData("", Secret)]
    [InlineData(null, Secret)]
    [InlineData("1234", Secret)]
    [InlineData("12345678901234567890123456789012345", Secret)]
    [InlineData("12345abc", Secret)]
    [InlineData("123 456", Secret)]
    public async Task InvalidAppId_IsRejected_AndNothingIsSaved(string? appId, string secret)
    {
        var result = await Service().SaveAsync(appId, secret, false, Admin, null);

        Assert.False(result.Succeeded);
        Assert.Equal("The App ID is the number shown in the Meta app dashboard (App settings → Basic).", result.Error);
        Assert.Empty(await Rows());
        Assert.Empty(_audit.Entries);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789 abcdef")]
    [InlineData("0123456789\tabcdef")]
    public async Task InvalidSecret_IsRejected(string secret)
    {
        var result = await Service().SaveAsync(AppId, secret, false, Admin, null);

        Assert.False(result.Succeeded);
        Assert.Equal("The App secret must be 16 to 128 characters long, with no spaces.", result.Error);
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task SecretBounds_16And128_AreAccepted_And129IsNot()
    {
        Assert.True((await Service().SaveAsync(AppId, new string('a', 16), false, Admin, null)).Succeeded);
        Assert.True((await Service().SaveAsync(AppId, new string('b', 128), false, Admin, null)).Succeeded);
        var tooLong = await Service().SaveAsync(AppId, new string('c', 129), false, Admin, null);

        Assert.False(tooLong.Succeeded);
        Assert.Equal(new string('b', 128), (await Service().GetCredentialsAsync())!.AppSecret);
    }

    [Fact]
    public async Task FirstSave_WithoutASecret_NeedsOne()
    {
        var result = await Service().SaveAsync(AppId, "  ", false, Admin, null);

        Assert.False(result.Succeeded);
        Assert.Equal("Enter the App secret.", result.Error);
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task BlankSecret_KeepsTheStoredOne()
    {
        await Save();

        var result = await Service().SaveAsync("999999", "", false, Admin, null);

        Assert.True(result.Succeeded);
        Assert.Equal(new InstagramAppCredentials("999999", Secret), await Service().GetCredentialsAsync());
    }

    [Fact]
    public async Task TheSecretIsStoredProtected()
    {
        await Save();

        var rows = await Rows();

        Assert.Equal(AppId, rows[InstagramAppSettingKeys.AppId]);
        Assert.NotEqual(Secret, rows[InstagramAppSettingKeys.AppSecret]);
        Assert.DoesNotContain(Secret, rows[InstagramAppSettingKeys.AppSecret]);
        Assert.True(_protector.TryUnprotect(rows[InstagramAppSettingKeys.AppSecret], out var plain));
        Assert.Equal(Secret, plain);
    }

    [Fact]
    public async Task AnUnreadableSecret_MeansNotConfigured_AndANewSecretFixesIt()
    {
        await Save();
        await Run(s => s.SetAsync(InstagramAppSettingKeys.AppSecret, "not-a-protected-value"));

        var status = await Service().GetStatusAsync();
        var blank = await Service().SaveAsync(AppId, "", false, Admin, null);

        Assert.Equal(new InstagramAppStatus(AppId, true, true, false), status);
        Assert.Null(await Service().GetCredentialsAsync());
        Assert.False(blank.Succeeded);
        Assert.Equal("Enter the App secret.", blank.Error);

        Assert.True((await Service().SaveAsync(AppId, Secret, false, Admin, null)).Succeeded);
        Assert.True((await Service().GetStatusAsync()).IsConfigured);
    }

    [Fact]
    public async Task ChangingTheAppId_WhileConnected_WithoutConfirmation_SavesNothing()
    {
        await Save();
        await SetConnected();
        var before = await Rows();
        _audit.Entries.Clear();

        var result = await Service().SaveAsync("999999", "", false, Admin, null);

        Assert.False(result.Succeeded);
        Assert.Equal("Changing the App ID removes the Instagram connection. Confirm to continue.", result.Error);
        Assert.Equal(before, await Rows());
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task ChangingTheAppId_WhileConnected_WithConfirmation_RemovesTheConnection_AndKeepsTheAppKeys()
    {
        await Save();
        await SetConnected();
        await Run(s => s.SetAsync(InstagramSettingKeys.LastFetchResult, "ok:3"));
        _audit.Entries.Clear();

        var result = await Service().SaveAsync("999999", "", true, Admin, "10.0.0.2");

        Assert.True(result.Succeeded);
        Assert.True(result.Disconnected);
        Assert.Equal(
            [
                InstagramAppSettingKeys.AppId, InstagramAppSettingKeys.AppSecret, InstagramSettingKeys.LastFetchResult,
            ],
            (await Rows()).Keys.Order().ToArray());
        Assert.Equal(new InstagramAppCredentials("999999", Secret), await Service().GetCredentialsAsync());
        Assert.Equal(
            ("instagram-app-saved", Admin, "10.0.0.2", "app-id-changed=true secret-replaced=false disconnected=true"),
            Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task ChangingTheAppId_WhileDisconnectedButTokenKept_AlsoNeedsConfirmation()
    {
        await Save();
        await SetConnected();
        await Run(s => s.SetAsync(InstagramSettingKeys.TokenInvalidAt, "2026-11-14T00:00:00.0000000Z"));

        var result = await Service().SaveAsync("999999", "", false, Admin, null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ChangingOnlyTheSecret_KeepsTheConnection()
    {
        await Save();
        await SetConnected();
        _audit.Entries.Clear();

        var result = await Service().SaveAsync(AppId, "NEW-SECRET-0123456789", false, Admin, null);

        Assert.True(result.Succeeded);
        Assert.False(result.Disconnected);
        Assert.Contains(InstagramSettingKeys.AccessToken, (await Rows()).Keys);
        Assert.Equal("NEW-SECRET-0123456789", (await Service().GetCredentialsAsync())!.AppSecret);
        Assert.Equal("app-id-changed=false secret-replaced=true disconnected=false", Assert.Single(_audit.Entries).Details);
    }

    [Fact]
    public async Task ASaveWithoutChanges_IsASuccessfulNoOp_AndNotAudited()
    {
        await Save();
        _audit.Entries.Clear();

        var same = await Service().SaveAsync(AppId, "", false, Admin, null);
        var sameWithSecret = await Service().SaveAsync(AppId, Secret, false, Admin, null);

        Assert.True(same.Succeeded);
        Assert.True(sameWithSecret.Succeeded);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Remove_ClearsTheAppAndTheConnection_AndAudits()
    {
        await Save();
        await SetConnected();
        await Run(s => s.SetAsync(InstagramSettingKeys.LastFetchResult, "ok:3"));
        _audit.Entries.Clear();

        await Service().RemoveAsync(Admin, "10.0.0.3");

        Assert.Equal([InstagramSettingKeys.LastFetchResult], (await Rows()).Keys.ToArray());
        Assert.Equal(new InstagramAppStatus(null, false, false, false), await Service().GetStatusAsync());
        Assert.Equal(("instagram-app-removed", Admin, "10.0.0.3", "disconnected=true"), Assert.Single(_audit.Entries));
    }

    [Fact]
    public async Task Remove_WithoutAConnection_AuditsDisconnectedFalse()
    {
        await Save();
        _audit.Entries.Clear();

        await Service().RemoveAsync(Admin, null);

        Assert.Empty(await Rows());
        Assert.Equal("disconnected=false", Assert.Single(_audit.Entries).Details);
    }

    [Fact]
    public async Task AuditDetails_NeverContainTheValues()
    {
        await Save();
        await SetConnected();
        await Service().SaveAsync("999999", "OTHER-SECRET-0123456789", true, Admin, null);
        await Service().RemoveAsync(Admin, null);

        foreach (var entry in _audit.Entries)
        {
            foreach (var value in new[] { AppId, Secret, "999999", "OTHER-SECRET-0123456789" })
            {
                Assert.DoesNotContain(value, entry.Details ?? "");
                Assert.DoesNotContain(value, entry.Event);
            }
        }
    }

    private async Task Save() =>
        Assert.True((await Service().SaveAsync(AppId, Secret, false, Admin, null)).Succeeded);

    private Task SetConnected() => Run(s => s.SetAsync(InstagramSettingKeys.AccessToken, _protector.Protect("TOKEN")));

    private InstagramAppSettingsService Service()
    {
        var context = CreateContext();
        return new InstagramAppSettingsService(new IntegrationSettingRepository(context), new UnitOfWork(context), _protector, _audit);
    }

    private async Task Run(Func<IIntegrationSettingRepository, Task> action)
    {
        await using var context = CreateContext();
        await action(new IntegrationSettingRepository(context));
        await context.SaveChangesAsync();
    }

    private async Task<Dictionary<string, string>> Rows()
    {
        await using var context = CreateContext();
        return await context.Set<IntegrationSetting>().ToDictionaryAsync(x => x.Key, x => x.Value);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options, _time);
}
