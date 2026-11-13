namespace SaronidaSailing.Core.Services;

public static class InstagramSettingKeys
{
    public const string AccessToken = "instagram.access-token";
    public const string TokenExpiresAt = "instagram.token-expires-at";
    public const string TokenIssuedAt = "instagram.token-issued-at";
    public const string UserId = "instagram.user-id";
    public const string Username = "instagram.username";
    public const string ConnectedAt = "instagram.connected-at";
    public const string ConnectedBy = "instagram.connected-by";
    public const string TokenInvalidAt = "instagram.token-invalid-at";

    public const string LastFetchAt = "instagram.last-fetch-at";
    public const string LastFetchResult = "instagram.last-fetch-result";
    public const string LastRefreshAt = "instagram.last-refresh-at";
    public const string LastRefreshResult = "instagram.last-refresh-result";

    public static readonly IReadOnlyList<string> All =
    [
        AccessToken, TokenExpiresAt, TokenIssuedAt, UserId, Username, ConnectedAt, ConnectedBy, TokenInvalidAt,
    ];
}
