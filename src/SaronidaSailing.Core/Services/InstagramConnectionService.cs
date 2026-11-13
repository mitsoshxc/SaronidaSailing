using System.Globalization;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class InstagramConnectionService(
    IInstagramApiClient client,
    IIntegrationSettingRepository settings,
    IUnitOfWork unitOfWork,
    ISecretProtector protector,
    IAuditLog audit,
    TimeProvider time) : IInstagramConnectionService
{
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
            await settings.SetAsync(InstagramSettingKeys.TokenExpiresAt, Format(longLived.ExpiresAt), ct);
            await settings.SetAsync(InstagramSettingKeys.UserId, profile.Id, ct);
            await settings.SetAsync(InstagramSettingKeys.Username, profile.Username, ct);
            await settings.SetAsync(InstagramSettingKeys.ConnectedAt, Format(time.GetUtcNow().UtcDateTime), ct);
            await settings.SetAsync(InstagramSettingKeys.ConnectedBy, adminEmail, ct);
            await settings.RemoveAsync(InstagramSettingKeys.TokenInvalidAt, ct);
            await unitOfWork.SaveChangesAsync(ct);

            audit.Record("instagram-connected", adminEmail, ipAddress, $"username=@{profile.Username}");
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

    private async Task<(InstagramStatus Status, string? Token)> ReadAsync(CancellationToken ct)
    {
        var canConnect = client.IsConfigured;
        var stored = await settings.GetAsync(InstagramSettingKeys.AccessToken, ct);
        if (stored is null)
        {
            return (new InstagramStatus(InstagramConnectionState.NotConnected, canConnect, null, null, null, null), null);
        }

        var username = await settings.GetAsync(InstagramSettingKeys.Username, ct);
        var expiresAt = Parse(await settings.GetAsync(InstagramSettingKeys.TokenExpiresAt, ct));
        var connectedAt = Parse(await settings.GetAsync(InstagramSettingKeys.ConnectedAt, ct));
        var connectedBy = await settings.GetAsync(InstagramSettingKeys.ConnectedBy, ct);
        var invalidAt = await settings.GetAsync(InstagramSettingKeys.TokenInvalidAt, ct);

        string? token = null;
        var usable = invalidAt is null
            && expiresAt is { } expiry && expiry > time.GetUtcNow().UtcDateTime
            && protector.TryUnprotect(stored, out token);
        var state = usable ? InstagramConnectionState.Connected : InstagramConnectionState.Disconnected;

        return (new InstagramStatus(state, canConnect, username, expiresAt, connectedAt, connectedBy), usable ? token : null);
    }

    private static string Format(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime? Parse(string? value) =>
        value is not null && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
