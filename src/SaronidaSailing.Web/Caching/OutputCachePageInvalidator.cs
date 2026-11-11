using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Web.Caching;

public sealed class OutputCachePageInvalidator(
    ICacheTagEvictor evictor,
    ICacheEvictionBus bus,
    ILogger<OutputCachePageInvalidator> logger) : IPageCacheInvalidator
{
    public async Task EvictPagesAsync(IReadOnlyCollection<string> pageKeys, CancellationToken ct = default)
    {
        string[] tags = pageKeys.Contains(ContentKeys.Shared)
            ? [CacheTags.AllPages]
            : [.. pageKeys.Select(CacheTags.Page)];

        await evictor.EvictTagsAsync(tags, ct);

        try
        {
            await bus.PublishAsync(tags, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Publishing cache eviction for tags {Tags} failed; other instances will catch up on expiry or reconnect.", string.Join(", ", tags));
        }
    }
}
