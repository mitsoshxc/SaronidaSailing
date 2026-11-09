using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Web.Caching;

public sealed class OutputCachePageInvalidator(IOutputCacheStore store) : IPageCacheInvalidator
{
    public async Task EvictPagesAsync(IReadOnlyCollection<string> pageKeys, CancellationToken ct = default)
    {
        if (pageKeys.Contains(ContentKeys.Shared))
        {
            await store.EvictByTagAsync(CacheTags.AllPages, ct);
            return;
        }

        foreach (var pageKey in pageKeys)
        {
            await store.EvictByTagAsync(CacheTags.Page(pageKey), ct);
        }
    }
}
