using System.Text.RegularExpressions;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed partial class InstagramAppSettingsService(
    IIntegrationSettingRepository settings,
    IUnitOfWork unitOfWork,
    ISecretProtector protector,
    IAuditLog audit) : IInstagramAppSettingsService
{
    public const string AppIdError = "The App ID is the number shown in the Meta app dashboard (App settings → Basic).";

    public const string SecretLengthError = "The App secret must be 16 to 128 characters long, with no spaces.";

    public const string SecretMissingError = "Enter the App secret.";

    public const string ConfirmError = "Changing the App ID removes the Instagram connection. Confirm to continue.";

    private const int SecretMinLength = 16;

    private const int SecretMaxLength = 128;

    [GeneratedRegex("^[0-9]{5,32}$")]
    private static partial Regex AppIdPattern();

    public async Task<InstagramAppStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var (appId, stored, secret) = await ReadAsync(ct);
        var unreadable = stored is not null && secret is null;
        return new InstagramAppStatus(appId, stored is not null, unreadable, appId is not null && secret is not null);
    }

    public async Task<InstagramAppCredentials?> GetCredentialsAsync(CancellationToken ct = default)
    {
        var (appId, _, secret) = await ReadAsync(ct);
        return appId is not null && secret is not null ? new InstagramAppCredentials(appId, secret) : null;
    }

    public async Task<InstagramAppSaveResult> SaveAsync(
        string? appId, string? newSecret, bool confirmDisconnect, string? adminEmail, string? ipAddress, CancellationToken ct = default)
    {
        var id = (appId ?? "").Trim();
        var secret = (newSecret ?? "").Trim();
        if (!AppIdPattern().IsMatch(id))
        {
            return Fail(AppIdError);
        }

        if (secret.Length > 0 && (secret.Length is < SecretMinLength or > SecretMaxLength || secret.Any(char.IsWhiteSpace)))
        {
            return Fail(SecretLengthError);
        }

        var (storedId, _, storedSecret) = await ReadAsync(ct);
        if (secret.Length == 0 && storedSecret is null)
        {
            return Fail(SecretMissingError);
        }

        var idChanged = storedId != id;
        var secretReplaced = secret.Length > 0 && secret != storedSecret;
        if (!idChanged && !secretReplaced)
        {
            return new InstagramAppSaveResult(true, null, false);
        }

        var disconnect = idChanged && await settings.GetAsync(InstagramSettingKeys.AccessToken, ct) is not null;
        if (disconnect && !confirmDisconnect)
        {
            return Fail(ConfirmError);
        }

        await settings.SetAsync(InstagramAppSettingKeys.AppId, id, ct);
        if (secretReplaced)
        {
            await settings.SetAsync(InstagramAppSettingKeys.AppSecret, protector.Protect(secret), ct);
        }

        if (disconnect)
        {
            await RemoveConnectionAsync(ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        audit.Record(
            "instagram-app-saved", adminEmail, ipAddress,
            $"app-id-changed={Flag(idChanged)} secret-replaced={Flag(secretReplaced)} disconnected={Flag(disconnect)}");
        return new InstagramAppSaveResult(true, null, disconnect);
    }

    public async Task RemoveAsync(string? adminEmail, string? ipAddress, CancellationToken ct = default)
    {
        var disconnected = await settings.GetAsync(InstagramSettingKeys.AccessToken, ct) is not null;
        await settings.RemoveAsync(InstagramAppSettingKeys.AppId, ct);
        await settings.RemoveAsync(InstagramAppSettingKeys.AppSecret, ct);
        await RemoveConnectionAsync(ct);
        await unitOfWork.SaveChangesAsync(ct);
        audit.Record("instagram-app-removed", adminEmail, ipAddress, $"disconnected={Flag(disconnected)}");
    }

    private async Task RemoveConnectionAsync(CancellationToken ct)
    {
        foreach (var key in InstagramSettingKeys.All)
        {
            await settings.RemoveAsync(key, ct);
        }
    }

    private async Task<(string? AppId, string? StoredSecret, string? Secret)> ReadAsync(CancellationToken ct)
    {
        var appId = (await settings.GetAsync(InstagramAppSettingKeys.AppId, ct))?.Trim();
        var stored = await settings.GetAsync(InstagramAppSettingKeys.AppSecret, ct);
        string? secret = null;
        if (stored is not null && protector.TryUnprotect(stored, out var plain) && plain.Length > 0)
        {
            secret = plain;
        }

        return (string.IsNullOrEmpty(appId) ? null : appId, stored, secret);
    }

    private static InstagramAppSaveResult Fail(string error) => new(false, error, false);

    private static string Flag(bool value) => value ? "true" : "false";
}
