using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
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
        app.MapMethods(RobotsPath, ["GET", "HEAD"], (
            HttpContext http, IHostEnvironment environment, IOptions<SiteOptions> site) =>
        {
            http.Response.Headers.CacheControl = CacheControl;
            var text = environment.IsPreview()
                ? "User-agent: *\nDisallow: /\n"
                : $"User-agent: *\nDisallow: /admin\n\nSitemap: {site.Value.BaseUrl}{SitemapPath}\n";
            return Results.Text(text, "text/plain; charset=utf-8");
        });

        app.MapMethods(SitemapPath, ["GET", "HEAD"], (
            HttpContext http, IHostEnvironment environment, IOptions<SiteOptions> site, ILocalizedUrlService urls) =>
        {
            if (environment.IsPreview())
            {
                return Results.NotFound();
            }

            http.Response.Headers.CacheControl = CacheControl;
            return Results.Bytes(BuildSitemap(site.Value.BaseUrl, urls), "application/xml; charset=utf-8");
        });

        return app;
    }

    private static byte[] BuildSitemap(string baseUrl, ILocalizedUrlService urls)
    {
        var root = new XElement(
            SitemapNamespace + "urlset",
            new XAttribute(XNamespace.Xmlns + "xhtml", XhtmlNamespace));

        foreach (var page in SitePages.All)
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
