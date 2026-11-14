using Microsoft.AspNetCore.Localization;

namespace SaronidaSailing.Web.Localization;

public sealed class UrlPrefixRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var culture = SiteCultures.ForPath(httpContext.Request.Path);
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture));
    }
}
