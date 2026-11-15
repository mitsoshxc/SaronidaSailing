using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Endpoints;

public static class SeoEndpoints
{
    public const string RobotsPath = "/robots.txt";

    public const string SitemapPath = "/sitemap.xml";

    private const string CacheControl = "public, max-age=3600";

    private static readonly XNamespace SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private static readonly XNamespace XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods(RobotsPath, ["GET", "HEAD"], async (
            HttpContext http, IHostEnvironment environment, IOptions<SiteOptions> site, ISiteSettingsService settings) =>
        {
            var isPreview = environment.IsPreview();
            var stored = isPreview ? null : await settings.GetRobotsTxtAsync(http.RequestAborted);
            http.Response.Headers.CacheControl = CacheControl;
            return Results.Text(RobotsTxt.Served(isPreview, stored, site.Value.BaseUrl), "text/plain; charset=utf-8");
        });

        app.MapMethods(SitemapPath, ["GET", "HEAD"], async (
            HttpContext http,
            IHostEnvironment environment,
            IOptions<SiteOptions> site,
            ILocalizedUrlService urls,
            IContentService content) =>
        {
            if (environment.IsPreview())
            {
                return Results.NotFound();
            }

            var noIndex = await content.GetNoIndexPagesAsync(http.RequestAborted);
            http.Response.Headers.CacheControl = CacheControl;
            return Results.Bytes(BuildSitemap(site.Value.BaseUrl, urls, noIndex), "application/xml; charset=utf-8");
        });

        return app;
    }

    private static byte[] BuildSitemap(string baseUrl, ILocalizedUrlService urls, IReadOnlySet<string> noIndex)
    {
        var root = new XElement(
            SitemapNamespace + "urlset",
            new XAttribute(XNamespace.Xmlns + "xhtml", XhtmlNamespace));

        foreach (var page in SitePages.All.Where(p => !noIndex.Contains(p.Key)))
        {
            foreach (var culture in SiteCultures.Supported)
            {
                root.Add(new XElement(
                    SitemapNamespace + "url",
                    new XElement(SitemapNamespace + "loc", baseUrl + urls.GetPath(page.Key, culture)),
                    Alternate(SiteCultures.Greek, baseUrl + urls.GetPath(page.Key, SiteCultures.Greek)),
                    Alternate(SiteCultures.English, baseUrl + urls.GetPath(page.Key, SiteCultures.English)),
                    Alternate("x-default", baseUrl + urls.GetPath(page.Key, SiteCultures.Greek))));
            }
        }

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(writer);
        }

        return stream.ToArray();
    }

    private static XElement Alternate(string language, string href) => new(
        XhtmlNamespace + "link",
        new XAttribute("rel", "alternate"),
        new XAttribute("hreflang", language),
        new XAttribute("href", href));
}
