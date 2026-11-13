using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class InstagramConnectionService(
    IInstagramApiClient client,
    IIntegrationSettingRepository settings,
    IUnitOfWork unitOfWork,
    ISecretProtector protector,
    IAuditLog audit,
    IInstagramJobQueue jobQueue,
    ILogger<InstagramConnectionService> logger,
    TimeProvider time) : IInstagramConnectionService
{
    private static readonly TimeSpan MinimumTokenAge = TimeSpan.FromHours(24);

    public async Task<InstagramStatus> GetStatusAsync(CancellationToken ct = default) =>
        (await ReadAsync(ct)).Status;

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default) =>
        (await ReadAsync(ct)).Token;

    public async Task<InstagramConnectResult> CompleteConnectAsync(
        string code, string redirectUri, string adminEmail, string? ipAddress, CancellationToken ct = default)
    {
        try
        {
            var shortLived = await client.ExchangeCodeAsync(code, redirectUri, ct);
            var longLived = await client.ExchangeForLongLivedAsync(shortLived.AccessToken, ct);
            var profile = await client.GetProfileAsync(longLived.AccessToken, ct);

            await settings.SetAsync(InstagramSettingKeys.AccessToken, protector.Protect(longLived.AccessToken), ct);
            await settings.SetAsync(InstagramSettingKeys.TokenExpiresAt, InstagramTimestamps.Format(longLived.ExpiresAt), ct);
            await settings.SetAsync(InstagramSettingKeys.TokenIssuedAt, InstagramTimestamps.Format(time.GetUtcNow().UtcDateTime), ct);
            await settings.SetAsync(InstagramSettingKeys.UserId, profile.Id, ct);
            await settings.SetAsync(InstagramSettingKeys.Username, profile.Username, ct);
            await settings.SetAsync(InstagramSettingKeys.ConnectedAt, InstagramTimestamps.Format(time.GetUtcNow().UtcDateTime), ct);
            await settings.SetAsync(InstagramSettingKeys.ConnectedBy, adminEmail, ct);
            await settings.RemoveAsync(InstagramSettingKeys.TokenInvalidAt, ct);
            await unitOfWork.SaveChangesAsync(ct);

            audit.Record("instagram-connected", adminEmail, ipAddress, $"username=@{profile.Username}");
            jobQueue.EnqueueFetch();
            return new InstagramConnectResult(true, profile.Username, null);
        }
        catch (InstagramApiException ex)
        {
            var details = $"kind={ex.Kind}" + (ex.ErrorCode is { } errorCode ? $" code={errorCode}" : "");
            audit.Record("instagram-connect-failed", adminEmail, ipAddress, details);
            return new InstagramConnectResult(false, null, ex.Kind);
        }
    }

    public async Task DisconnectAsync(string adminEmail, string? ipAddress, CancellationToken ct = default)
    {
        foreach (var key in InstagramSettingKeys.All)
        {
            await settings.RemoveAsync(key, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        audit.Record("instagram-disconnected", adminEmail, ipAddress);
    }

    public async Task MarkTokenInvalidAsync(CancellationToken ct = default)
    {
        await StageTokenInvalidAsync(ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<InstagramRefreshResult> RefreshTokenAsync(CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        if (token is null)
        {
            return await FinishRefreshAsync("skipped:not-connected", ct);
        }

        var issuedAt = InstagramTimestamps.Parse(await settings.GetAsync(InstagramSettingKeys.TokenIssuedAt, ct));
        if (issuedAt is { } issued && time.GetUtcNow().UtcDateTime - issued < MinimumTokenAge)
        {
            return await FinishRefreshAsync("skipped:too-recent", ct);
        }

        try
        {
            var renewed = await client.RefreshAsync(token, ct);
            var now = time.GetUtcNow().UtcDateTime;
            await settings.SetAsync(InstagramSettingKeys.AccessToken, protector.Protect(renewed.AccessToken), ct);
            await settings.SetAsync(InstagramSettingKeys.TokenExpiresAt, InstagramTimestamps.Format(renewed.ExpiresAt), ct);
            await settings.SetAsync(InstagramSettingKeys.TokenIssuedAt, InstagramTimestamps.Format(now), ct);
            logger.LogInformation("Instagram access token renewed, valid until {ExpiresAt:O}.", renewed.ExpiresAt);
            return await FinishRefreshAsync("ok", ct);
        }
        catch (InstagramApiException ex)
        {
            logger.LogWarning("Instagram token renewal failed: kind={Kind} code={Code}.", ex.Kind, ex.ErrorCode);
            if (ex.Kind == InstagramApiErrorKind.TokenInvalid)
            {
                await StageTokenInvalidAsync(ct);
            }

            return await FinishRefreshAsync($"failed:{ex.Kind}", ct);
        }
    }

    private async Task StageTokenInvalidAsync(CancellationToken ct) =>
        await settings.SetAsync(InstagramSettingKeys.TokenInvalidAt, InstagramTimestamps.Format(time.GetUtcNow().UtcDateTime), ct);

    private async Task<InstagramRefreshResult> FinishRefreshAsync(string code, CancellationToken ct)
    {
        await settings.SetAsync(InstagramSettingKeys.LastRefreshAt, InstagramTimestamps.Format(time.GetUtcNow().UtcDateTime), ct);
        await settings.SetAsync(InstagramSettingKeys.LastRefreshResult, code, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new InstagramRefreshResult(code);
    }

    private async Task<(InstagramStatus Status, string? Token)> ReadAsync(CancellationToken ct)
    {
        var canConnect = client.IsConfigured;
        var stored = await settings.GetAsync(InstagramSettingKeys.AccessToken, ct);
        if (stored is null)
        {
            return (new InstagramStatus(InstagramConnectionState.NotConnected, canConnect, null, null, null, null), null);
        }

        var username = await settings.GetAsync(InstagramSettingKeys.Username, ct);
        var expiresAt = InstagramTimestamps.Parse(await settings.GetAsync(InstagramSettingKeys.TokenExpiresAt, ct));
        var connectedAt = InstagramTimestamps.Parse(await settings.GetAsync(InstagramSettingKeys.ConnectedAt, ct));
        var connectedBy = await settings.GetAsync(InstagramSettingKeys.ConnectedBy, ct);
        var invalidAt = await settings.GetAsync(InstagramSettingKeys.TokenInvalidAt, ct);

        string? token = null;
        var usable = invalidAt is null
            && expiresAt is { } expiry && expiry > time.GetUtcNow().UtcDateTime
            && protector.TryUnprotect(stored, out token);
        var state = usable ? InstagramConnectionState.Connected : InstagramConnectionState.Disconnected;

        return (new InstagramStatus(state, canConnect, username, expiresAt, connectedAt, connectedBy), usable ? token : null);
    }
}
