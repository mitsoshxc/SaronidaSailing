namespace SaronidaSailing.Core.Interfaces;

public sealed record InstagramAppStatus(string? AppId, bool SecretSaved, bool SecretUnreadable, bool IsConfigured);

public sealed record InstagramAppSaveResult(bool Succeeded, string? Error, bool Disconnected);

public interface IInstagramAppSettingsService
{
    Task<InstagramAppStatus> GetStatusAsync(CancellationToken ct = default);

    Task<InstagramAppCredentials?> GetCredentialsAsync(CancellationToken ct = default);

    Task<InstagramAppSaveResult> SaveAsync(
        string? appId, string? newSecret, bool confirmDisconnect, string? adminEmail, string? ipAddress, CancellationToken ct = default);

    Task RemoveAsync(string? adminEmail, string? ipAddress, CancellationToken ct = default);
}
