namespace SaronidaSailing.Core.Interfaces;

public enum InstagramConnectionState
{
    NotConnected,
    Connected,
    Disconnected
}

public sealed record InstagramStatus(
    InstagramConnectionState State,
    bool CanConnect,
    string? Username,
    DateTime? ExpiresAt,
    DateTime? ConnectedAt,
    string? ConnectedBy);

public sealed record InstagramRefreshResult(string Code);

public sealed record InstagramConnectResult(bool Succeeded, string? Username, InstagramApiErrorKind? ErrorKind);

public interface IInstagramConnectionService
{
    Task<InstagramStatus> GetStatusAsync(CancellationToken ct = default);

    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);

    Task<InstagramConnectResult> CompleteConnectAsync(
        string code, string redirectUri, string adminEmail, string? ipAddress, CancellationToken ct = default);

    Task DisconnectAsync(string adminEmail, string? ipAddress, CancellationToken ct = default);

    Task MarkTokenInvalidAsync(CancellationToken ct = default);

    Task<InstagramRefreshResult> RefreshTokenAsync(CancellationToken ct = default);
}
