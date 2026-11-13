using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Web.Models;

namespace SaronidaSailing.Web.Caching;

public sealed class TripsVaryByPolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var requested = context.HttpContext.Request.Query[TripTabs.QueryKey].ToString();
        context.CacheVaryByRules.VaryByValues[TripTabs.QueryKey] = TripTabs.Normalize(requested);
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;
}
