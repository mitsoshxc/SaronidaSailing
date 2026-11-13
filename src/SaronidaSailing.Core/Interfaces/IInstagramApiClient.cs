namespace SaronidaSailing.Core.Interfaces;

public interface IInstagramApiClient
{
    string BuildAuthorizeUrl(string redirectUri, string state);

    Task<InstagramShortLivedToken> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    Task<InstagramLongLivedToken> ExchangeForLongLivedAsync(string shortLivedToken, CancellationToken ct = default);

    Task<InstagramLongLivedToken> RefreshAsync(string longLivedToken, CancellationToken ct = default);

    Task<InstagramProfile> GetProfileAsync(string token, CancellationToken ct = default);

    Task<IReadOnlyList<InstagramMediaItem>> GetRecentMediaAsync(string token, int limit, CancellationToken ct = default);

    Task<Stream> DownloadImageAsync(string url, CancellationToken ct = default);
}
