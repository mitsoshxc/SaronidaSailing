using Microsoft.Extensions.Options;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Redirects;

public sealed class OldSiteRedirectMiddleware(RequestDelegate next, IOptions<SiteOptions> site)
{
    public Task InvokeAsync(HttpContext context, ILocalizedUrlService urls)
    {
        var request = context.Request;
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || !OldSiteRedirects.IsOldSitePath(request.Path))
        {
            return next(context);
        }

        if (OldSiteRedirects.IsGone(request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            return Task.CompletedTask;
        }

        var target = OldSiteRedirects.Resolve(request.Path);
        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = site.Value.BaseUrl + urls.GetPath(target.PageKey, target.Culture);
        return Task.CompletedTask;
    }
}

public static class OldSiteRedirectExtensions
{
    public static IApplicationBuilder UseOldSiteRedirects(this IApplicationBuilder app) =>
        app.UseMiddleware<OldSiteRedirectMiddleware>();
}
