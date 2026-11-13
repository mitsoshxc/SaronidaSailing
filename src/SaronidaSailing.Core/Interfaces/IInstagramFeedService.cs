namespace SaronidaSailing.Core.Interfaces;

public sealed record InstagramFetchResult(string Code);

public sealed record InstagramActivity(
    int PostCount,
    DateTime? LastFetchAt,
    string? LastFetchResult,
    DateTime? LastRefreshAt,
    string? LastRefreshResult)
{
    public bool HasAnything => PostCount > 0 || LastFetchAt is not null || LastRefreshAt is not null;
}

public sealed record InstagramFeedPost(string Permalink, string? Caption, DateTime PostedAt, PageImage Image);

public interface IInstagramFeedService
{
    Task<InstagramFetchResult> FetchAsync(CancellationToken ct = default);

    Task<InstagramActivity> GetActivityAsync(CancellationToken ct = default);

    Task<IReadOnlyList<InstagramFeedPost>> GetFeedAsync(int take, CancellationToken ct = default);
}
