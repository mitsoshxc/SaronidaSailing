using Microsoft.AspNetCore.Localization;

namespace SaronidaSailing.Web.Localization;

public sealed class UrlPrefixRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var culture = httpContext.Request.Path.StartsWithSegments(
            $"/{SiteCultures.EnglishPrefix}", StringComparison.OrdinalIgnoreCase)
            ? SiteCultures.English
            : SiteCultures.Greek;

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture));
    }
}
