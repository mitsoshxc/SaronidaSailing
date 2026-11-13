namespace SaronidaSailing.Core.Interfaces;

public sealed record InstagramShortLivedToken(string AccessToken, string UserId);

public sealed record InstagramLongLivedToken(string AccessToken, DateTime ExpiresAt);

public sealed record InstagramProfile(string Id, string Username);

public sealed record InstagramMediaItem(
    string Id,
    string? Caption,
    string MediaType,
    string Permalink,
    DateTime PostedAt,
    string ImageUrl);
