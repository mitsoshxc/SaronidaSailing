using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Caching;

public sealed class OutputCacheTagEvictor(IOutputCacheStore store) : ICacheTagEvictor
{
    public async Task EvictTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct = default)
    {
        foreach (var tag in tags)
        {
            await store.EvictByTagAsync(tag, ct);
        }
    }

    public Task EvictAllAsync(CancellationToken ct = default) => EvictTagsAsync([CacheTags.AllPages], ct);
}
