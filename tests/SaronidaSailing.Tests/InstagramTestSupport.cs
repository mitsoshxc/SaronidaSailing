using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Tests;

public sealed class FakeInstagramJobQueue : IInstagramJobQueue
{
    public int FetchCount { get; private set; }

    public void EnqueueFetch() => FetchCount++;

    public void Reset() => FetchCount = 0;
}

public sealed class InstagramAuditLog : IAuditLog
{
    public List<(string Event, string? User, string? Ip, string? Details)> Entries { get; } = [];

    public void Record(string eventName, string? userName, string? ipAddress, string? details = null) =>
        Entries.Add((eventName, userName, ipAddress, details));
}

internal sealed class RecordingPageCache : IPageCacheInvalidator
{
    public List<string[]> Evictions { get; } = [];

    public Task EvictPagesAsync(IReadOnlyCollection<string> pageKeys, CancellationToken ct = default)
    {
        Evictions.Add([.. pageKeys]);
        return Task.CompletedTask;
    }
}

internal sealed class StubInstagramClient : IInstagramApiClient
{
    public const string Token = "LONG-LIVED-TOKEN-123";

    private int _seed;

    public bool IsConfigured { get; set; } = true;

    public DateTime ExpiresAt { get; set; }

    public List<InstagramMediaItem> Items { get; } = [];

    public Dictionary<string, byte[]> Images { get; } = [];

    public InstagramApiException? MediaFailure { get; set; }

    public Dictionary<string, InstagramApiException> DownloadFailures { get; } = [];

    public int MediaCalls { get; private set; }

    public List<string> Downloaded { get; } = [];

    public InstagramLongLivedToken? Renewed { get; set; }

    public InstagramApiException? RefreshFailure { get; set; }

    public List<string> RefreshedTokens { get; } = [];

    public InstagramMediaItem AddPost(string id, string? caption = "caption", int minutesAgo = 0)
    {
        var item = new InstagramMediaItem(
            id, caption, "IMAGE", $"https://www.instagram.com/p/{id}/", new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo),
            $"https://cdn.example.test/{id}.jpg");
        Items.Add(item);
        Images[item.ImageUrl] = MediaTestImages.Photo(600, 600, seed: ++_seed);
        return item;
    }

    public void Replace(string id, Func<InstagramMediaItem, InstagramMediaItem> change)
    {
        var index = Items.FindIndex(x => x.Id == id);
        Items[index] = change(Items[index]);
    }

    public string BuildAuthorizeUrl(string redirectUri, string state) => throw new NotSupportedException();

    public Task<InstagramShortLivedToken> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
        Task.FromResult(new InstagramShortLivedToken("SHORT-TOKEN", "17841400000000001"));

    public Task<InstagramLongLivedToken> ExchangeForLongLivedAsync(string shortLivedToken, CancellationToken ct = default) =>
        Task.FromResult(new InstagramLongLivedToken(Token, ExpiresAt));

    public Task<InstagramLongLivedToken> RefreshAsync(string longLivedToken, CancellationToken ct = default)
    {
        RefreshedTokens.Add(longLivedToken);
        return RefreshFailure is not null
            ? Task.FromException<InstagramLongLivedToken>(RefreshFailure)
            : Task.FromResult(Renewed ?? throw new InvalidOperationException("Renewed token not set."));
    }

    public Task<InstagramProfile> GetProfileAsync(string token, CancellationToken ct = default) =>
        Task.FromResult(new InstagramProfile("17841400000000001", "saronida"));

    public Task<IReadOnlyList<InstagramMediaItem>> GetRecentMediaAsync(string token, int limit, CancellationToken ct = default)
    {
        MediaCalls++;
        return MediaFailure is not null
            ? Task.FromException<IReadOnlyList<InstagramMediaItem>>(MediaFailure)
            : Task.FromResult<IReadOnlyList<InstagramMediaItem>>([.. Items.Take(limit)]);
    }

    public Task<Stream> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        Downloaded.Add(url);
        if (DownloadFailures.TryGetValue(url, out var failure))
        {
            return Task.FromException<Stream>(failure);
        }

        return Task.FromResult<Stream>(new MemoryStream(Images[url]));
    }
}
