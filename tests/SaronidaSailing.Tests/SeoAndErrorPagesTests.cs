using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

public sealed class ErrorEndpointsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use(async (context, nextMiddleware) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (path.EndsWith("/test/throw", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Test failure.");
            }

            if (path.EndsWith("/test/gone", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 410;
                return;
            }

            await nextMiddleware(context);
        });
    };
}

public class ErrorPagesWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    protected override bool? MigrateAtStartup => true;

    protected virtual string EnvironmentName => Environments.Production;

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, ErrorEndpointsStartupFilter>();
            ConfigureTestServices(services);
        });
    }
}

public sealed class PreviewWebApplicationFactory : ErrorPagesWebApplicationFactory
{
    protected override string EnvironmentName => "Preview";
}

public class ThrowingContentProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException("The database must not be read.");
}

public sealed class NoContentWebApplicationFactory : ErrorPagesWebApplicationFactory
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddScoped(_ => DispatchProxy.Create<IContentService, ThrowingContentProxy>());
}

[Collection(WebHostCollection.Name)]
public class RobotsAndSitemapTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

    [Fact]
    public async Task Robots_DisallowsAdmin_AndPointsToTheSitemap()
    {
        var response = await NewClient(factory).GetAsync("/robots.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl!.ToString());
        Assert.Equal(
            "User-agent: *\nDisallow: /admin\n\nSitemap: https://localhost/sitemap.xml\n",
            await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task Sitemap_ListsEveryPageInBothLanguages_WithAlternates()
    {
        var response = await NewClient(factory).GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl!.ToString());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var urls = document.Root!.Elements(Sitemap + "url").ToList();

        Assert.Equal(14, urls.Count);
        var locations = urls.Select(u => u.Element(Sitemap + "loc")!.Value).ToList();
        Assert.All(locations, l => Assert.StartsWith("https://localhost/", l));
        Assert.Contains("https://localhost/", locations);
        Assert.Contains("https://localhost/en/", locations);
        Assert.Contains("https://localhost/ekdromes", locations);
        Assert.Contains("https://localhost/en/trips", locations);
        Assert.Equal(14, locations.Distinct().Count());

        foreach (var url in urls)
        {
            Assert.Equal(["loc", "link", "link", "link"], url.Elements().Select(e => e.Name.LocalName));
            var links = url.Elements(Xhtml + "link").ToDictionary(l => l.Attribute("hreflang")!.Value, l => l.Attribute("href")!.Value);
            Assert.Equal(["el", "en", "x-default"], links.Keys.Order());
            Assert.Equal(links["el"], links["x-default"]);
            Assert.StartsWith("https://localhost/en", links["en"]);
            Assert.Contains(url.Element(Sitemap + "loc")!.Value, links.Values);
        }
    }

    [Fact]
    public async Task PageResponse_HasNoRobotsHeader()
    {
        var response = await NewClient(factory).GetAsync("/");

        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }
}

[Collection(WebHostCollection.Name)]
public class PreviewSeoTests(PreviewWebApplicationFactory factory) : IClassFixture<PreviewWebApplicationFactory>
{
    [Fact]
    public async Task Robots_DisallowsEverything_WithoutASitemap()
    {
        var response = await NewClient(factory).GetAsync("/robots.txt");

        Assert.Equal("User-agent: *\nDisallow: /\n", await response.Content.ReadAsStringAsync());
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task Sitemap_IsNotFound()
    {
        var response = await NewClient(factory).GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/en/trips")]
    [InlineData("/no-such-page")]
    [InlineData("/test/throw")]
    public async Task EveryResponse_CarriesNoIndexNoFollow(string path)
    {
        var response = await NewClient(factory).GetAsync(path);

        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
    }
}

[Collection(WebHostCollection.Name)]
public class ErrorPagesTests(ErrorPagesWebApplicationFactory factory) : IClassFixture<ErrorPagesWebApplicationFactory>
{
    private static string[] ActionLinks(string html)
    {
        var actions = Regex.Match(html, "<div class=\"error-page__actions\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        return [.. Regex.Matches(actions, "href=\"([^\"]*)\"").Select(m => m.Groups[1].Value)];
    }

    private static void AssertNotFoundPage(string html, HttpResponseMessage response, string lang, string title, string[] paths)
    {
        Assert.Contains($"<html lang=\"{lang}\">", html);
        Assert.Contains($"<h1 class=\"error-page__title\">{title}</h1>", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", html);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(paths, ActionLinks(html));
        Assert.Contains("btn btn--cta", Regex.Match(html, "<a class=\"btn[^>]*>").Value);
        Assert.DoesNotContain("<header class=\"site-header", html);
        Assert.DoesNotContain("<footer", html);
        Assert.DoesNotContain("cms-edit", html);
        Assert.DoesNotContain("application/ld+json", html);
        Assert.DoesNotContain("rel=\"canonical\"", html);
    }

    [Fact]
    public async Task UnknownGreekPath_ShowsTheGreekNotFoundPage()
    {
        var response = await NewClient(factory).GetAsync("/nothing-here");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNotFoundPage(html, response, "el", "Η σελίδα δεν βρέθηκε", ["/", "/ekdromes", "/epikoinonia"]);
        Assert.Contains("Ίσως άλλαξε η διεύθυνση ή η σελίδα δεν υπάρχει πια.", html);
    }

    [Theory]
    [InlineData("/en/nothing-here")]
    [InlineData("/en/skafos")]
    [InlineData("/EN/x/y")]
    public async Task UnknownEnglishPath_ShowsTheEnglishNotFoundPage(string path)
    {
        var response = await NewClient(factory).GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNotFoundPage(html, response, "en", "Page not found", ["/en/", "/en/trips", "/en/contact"]);
        Assert.Contains("The address may have changed, or the page no longer exists.", html);
    }

    [Fact]
    public async Task CrossLanguageSlug_ShowsTheGreekNotFoundPage()
    {
        var response = await NewClient(factory).GetAsync("/boat");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Η σελίδα δεν βρέθηκε", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NotFoundPage_IsNotOutputCached()
    {
        var first = await (await NewClient(factory).GetAsync("/nothing-here-a")).Content.ReadAsStringAsync();
        var second = await NewClient(factory).GetAsync("/nothing-here-a");

        Assert.Null(second.Headers.Age);
        Assert.True(second.Headers.CacheControl!.NoStore);
        Assert.Equal(first, await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DirectVisit_RendersWithItsOwnStatus()
    {
        var notFound = await NewClient(factory).GetAsync("/error/404");
        var gone = await NewClient(factory).GetAsync("/error/410");
        var serverError = await NewClient(factory).GetAsync("/error/500");

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, serverError.StatusCode);
        Assert.Contains("Η σελίδα δεν βρέθηκε", await gone.Content.ReadAsStringAsync());
        Assert.Contains("Κάτι πήγε στραβά", await serverError.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await NewClient(factory).GetAsync("/error/403")).StatusCode);
    }

    [Theory]
    [InlineData("/test/gone", "Η σελίδα δεν βρέθηκε", "el")]
    [InlineData("/en/test/gone", "Page not found", "en")]
    public async Task Gone_ShowsTheNotFoundPage_WithStatus410(string path, string title, string lang)
    {
        var response = await NewClient(factory).GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Contains($"<html lang=\"{lang}\">", html);
        Assert.Contains(title, html);
    }

    [Theory]
    [InlineData("/test/throw", "Κάτι πήγε στραβά", "Δοκιμάστε ξανά σε λίγα λεπτά.", "el", "/")]
    [InlineData("/en/test/throw", "Something went wrong", "Please try again in a few minutes.", "en", "/en/")]
    public async Task UnhandledException_ShowsTheServerErrorPage(string path, string title, string text, string lang, string home)
    {
        var response = await NewClient(factory).GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains($"<html lang=\"{lang}\">", html);
        Assert.Contains($"<h1 class=\"error-page__title\">{title}</h1>", html);
        Assert.Contains(text, html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", html);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal([home], ActionLinks(html));
    }

    [Fact]
    public async Task ServerErrorPage_AnswersEveryMethod_BecauseTheExceptionHandlerKeepsTheMethod()
    {
        var response = await NewClient(factory).PostAsync("/error/500", new StringContent(""));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Κάτι πήγε στραβά", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET", "/admin/api/unknown", HttpStatusCode.NotFound)]
    [InlineData("GET", "/admin/api/content/history", HttpStatusCode.Unauthorized)]
    [InlineData("POST", "/admin/api/content", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/media/missing.webp", HttpStatusCode.NotFound)]
    [InlineData("GET", "/sitemap.xml/extra", HttpStatusCode.NotFound)]
    public async Task ApiMediaAndSeoPaths_KeepTheirEmptyBodies(string method, string path, HttpStatusCode expected)
    {
        var response = await NewClient(factory).SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }
}

[Collection(WebHostCollection.Name)]
public class ErrorPagesWithoutDatabaseTests(NoContentWebApplicationFactory factory) : IClassFixture<NoContentWebApplicationFactory>
{
    [Theory]
    [InlineData("/nothing-here", "Η σελίδα δεν βρέθηκε")]
    [InlineData("/en/nothing-here", "Page not found")]
    public async Task NotFoundPage_ReadsNoContent(string path, string title)
    {
        var response = await NewClient(factory).GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/test/throw", "Κάτι πήγε στραβά")]
    [InlineData("/en/test/throw", "Something went wrong")]
    public async Task ServerErrorPage_ReadsNoContent(string path, string title)
    {
        var response = await NewClient(factory).GetAsync(path);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SitePages_AreBroken_ProvingTheStubWorks()
    {
        var response = await NewClient(factory).GetAsync("/");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Κάτι πήγε στραβά", await response.Content.ReadAsStringAsync());
    }
}

[Collection(WebHostCollection.Name)]
public class JsonLdTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static readonly ContentEditor Editor = new("jsonld-test@example.com", null);

    private static readonly string[] EveryPage =
    [
        "/", "/ekdromes", "/skafos", "/epikoinonia", "/oroi", "/aporrito", "/prosvasimotita",
        "/en/", "/en/trips", "/en/boat", "/en/contact", "/en/terms", "/en/privacy", "/en/accessibility",
    ];

    public static IEnumerable<object[]> Pages => EveryPage.Select(p => new object[] { p });

    private static JsonElement? ReadLd(string html)
    {
        var match = Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline);
        return match.Success ? JsonDocument.Parse(match.Groups[1].Value).RootElement.Clone() : null;
    }

    private async Task<JsonElement> LdOf(string path) =>
        ReadLd(await NewClient(factory).GetStringAsync(path)) ?? throw new Xunit.Sdk.XunitException($"No JSON-LD on {path}.");

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task EverySitePage_HasTheBusinessData(string path)
    {
        var english = path.StartsWith("/en", StringComparison.Ordinal);
        var ld = await LdOf(path);

        Assert.Equal("https://schema.org", ld.GetProperty("@context").GetString());
        Assert.Equal("LocalBusiness", ld.GetProperty("@type").GetString());
        Assert.Equal("Saronida Sailing", ld.GetProperty("name").GetString());
        Assert.Equal(english ? "https://localhost/en/" : "https://localhost/", ld.GetProperty("url").GetString());
        Assert.Equal(
            english
                ? "Private catamaran trips on a Lagoon 500 from Anavyssos."
                : "Ιδιωτικές εκδρομές με καταμαράν Lagoon 500 από την Ανάβυσσο.",
            ld.GetProperty("description").GetString());
        Assert.Equal(
            ["+306940057799", "+306978856418"],
            ld.GetProperty("telephone").EnumerateArray().Select(e => e.GetString()));
        var address = ld.GetProperty("address");
        Assert.Equal("PostalAddress", address.GetProperty("@type").GetString());
        Assert.Equal("GR", address.GetProperty("addressCountry").GetString());
        Assert.Equal(
            english ? "Agios Nikolaos, Anavyssos" : "Άγιος Νικόλαος, Ανάβυσσος",
            address.GetProperty("streetAddress").GetString());
        Assert.Equal(
            ["https://www.instagram.com/saronidasailing/", "https://www.facebook.com/Saronida-Sailing-108336665074413/"],
            ld.GetProperty("sameAs").EnumerateArray().Select(e => e.GetString()));
        Assert.False(ld.TryGetProperty("email", out _));
    }

    private async Task WithField(string key, string locale, ContentType type, string value, Func<Task> body)
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        var original = (await content.GetPageContentAsync("home", locale)).GetField(key)!.Value;
        Assert.True((await content.ApplyChangesAsync([new SetField(key, locale, type, value)], Editor)).Succeeded);
        try
        {
            await body();
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync([new SetField(key, locale, type, original)], Editor)).Succeeded);
        }
    }

    [Fact]
    public async Task RealEmail_IsEmitted()
    {
        var link = new ContentLink("mailto:info@saronidasailing.gr", "info@saronidasailing.gr").ToJson();

        await WithField("shared.contact.email", "el", ContentType.Link, link, async () =>
            Assert.Equal("info@saronidasailing.gr", (await LdOf("/")).GetProperty("email").GetString()));
    }

    [Fact]
    public async Task ValueWithAScriptEnd_CannotBreakOutOfTheBlock()
    {
        const string street = "Odos </script><script>alert(1)</script> & <b>1</b>";

        await WithField("shared.contact.address", "el", ContentType.Text, street, async () =>
        {
            var html = await NewClient(factory).GetStringAsync("/");
            var ld = ReadLd(html)!.Value;

            Assert.Equal(street, ld.GetProperty("address").GetProperty("streetAddress").GetString());
            Assert.DoesNotContain("<script>alert(1)", html);
        });
    }

    [Fact]
    public async Task PlaceholderValue_IsOmitted()
    {
        await WithField("shared.contact.address", "el", ContentType.Text, "[ADDRESS]", async () =>
            Assert.False((await LdOf("/")).TryGetProperty("address", out _)));
    }

    [Fact]
    public async Task AdminAndErrorPages_HaveNoJsonLd()
    {
        var login = await NewClient(factory).GetStringAsync("/admin/login");
        var notFound = await NewClient(factory).GetAsync("/nothing-here");

        Assert.DoesNotContain("application/ld+json", login);
        Assert.DoesNotContain("application/ld+json", await notFound.Content.ReadAsStringAsync());
    }
}
