using SaronidaSailing.Web.Endpoints;

namespace SaronidaSailing.Web;

public static class ErrorPageStatus
{
    private static readonly string[] PlainPaths =
        ["/admin/api", "/health", "/media", SeoEndpoints.SitemapPath, SeoEndpoints.RobotsPath];

    public static bool UsesErrorPage(HttpContext context) =>
        context.Response.StatusCode is StatusCodes.Status404NotFound or StatusCodes.Status410Gone
        && !PlainPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}
