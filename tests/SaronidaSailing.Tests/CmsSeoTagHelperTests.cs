using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web;
using SaronidaSailing.Web.Localization;
using SaronidaSailing.Web.TagHelpers;
using static SaronidaSailing.Tests.TagHelperTestSupport;

namespace SaronidaSailing.Tests;

public sealed class CmsSeoTagHelperTests
{
    private static async Task<string> Run(
        PageContent content, string? pagePath = "/Boat", string? fallbackTitle = "Boat · Saronida Sailing")
    {
        var httpContext = new DefaultHttpContext();
        if (pagePath is not null)
        {
            httpContext.Request.RouteValues["page"] = pagePath;
        }

        var urlService = new LocalizedUrlService(new HttpContextAccessor { HttpContext = httpContext });
        var helper = new CmsSeoTagHelper(
            new FakePageContentAccessor(content),
            urlService,
            Options.Create(new SiteOptions { BaseUrl = "https://localhost:5001" }),
            NullLogger<CmsSeoTagHelper>.Instance)
        {
            FallbackTitle = fallbackTitle,
        };

        var attributes = new TagHelperAttributeList();
        var context = NewContext("cms-seo", attributes);
        var output = NewOutput("cms-seo", attributes);
        await helper.ProcessAsync(context, output);
        return Render(output);
    }

    private static PageContent BoatContent(string locale, Dictionary<string, ContentValue> fields) =>
        new("boat", locale, fields, new Dictionary<string, IReadOnlyList<ContentListEntry>>(), new Dictionary<string, bool>());

    [Fact]
    public async Task SitePage_Greek_EmitsAllTagsInOrder()
    {
        var content = BoatContent("el", new()
        {
            ["boat.seo.title"] = Text("Το σκάφος"),
            ["boat.seo.description"] = Text("Περιγραφή"),
        });

        var html = await Run(content);

        var expected = string.Join(
            "\n    ",
            "<title>Το σκάφος</title>",
            "<meta name=\"description\" content=\"Περιγραφή\" />",
            "<meta property=\"og:title\" content=\"Το σκάφος\" />",
            "<meta property=\"og:description\" content=\"Περιγραφή\" />",
            "<meta property=\"og:type\" content=\"website\" />",
            "<meta property=\"og:site_name\" content=\"Saronida Sailing\" />",
            "<meta property=\"og:url\" content=\"https://localhost:5001/skafos\" />",
            "<meta property=\"og:locale\" content=\"el_GR\" />",
            "<meta property=\"og:locale:alternate\" content=\"en_GB\" />");
        Assert.Equal(expected, html);
    }

    [Fact]
    public async Task SitePage_English_UsesEnglishUrlAndLocales()
    {
        var content = BoatContent("en", new() { ["boat.seo.title"] = Text("The boat") });

        var html = await Run(content);

        Assert.Contains("content=\"https://localhost:5001/en/boat\"", html);
        Assert.Contains("<meta property=\"og:locale\" content=\"en_GB\" />", html);
        Assert.Contains("<meta property=\"og:locale:alternate\" content=\"el_GR\" />", html);
    }

    [Fact]
    public async Task HomePage_UrlIsRootPath()
    {
        var greek = await Run(Content("el"), "/Index");
        var english = await Run(Content("en"), "/Index");

        Assert.Contains("content=\"https://localhost:5001/\"", greek);
        Assert.Contains("content=\"https://localhost:5001/en/\"", english);
    }

    [Fact]
    public async Task MissingDescription_OmitsDescriptionTags()
    {
        var content = BoatContent("el", new() { ["boat.seo.title"] = Text("T") });

        var html = await Run(content);

        Assert.DoesNotContain("name=\"description\"", html);
        Assert.DoesNotContain("og:description", html);
    }

    [Fact]
    public async Task MissingTitle_UsesFallbackTitle_InTitleAndOgTitle()
    {
        var html = await Run(BoatContent("el", []));

        Assert.StartsWith("<title>Boat · Saronida Sailing</title>", html);
        Assert.Contains("<meta property=\"og:title\" content=\"Boat · Saronida Sailing\" />", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task EmptyFallback_UsesSiteName(string? fallback)
    {
        var siteHtml = await Run(BoatContent("el", []), fallbackTitle: fallback);
        var otherHtml = await Run(BoatContent("el", []), pagePath: null, fallbackTitle: fallback);

        Assert.StartsWith("<title>Saronida Sailing</title>", siteHtml);
        Assert.Equal("<title>Saronida Sailing</title>", otherHtml);
    }

    [Fact]
    public async Task NonTextTitle_IsIgnored()
    {
        var content = BoatContent("el", new() { ["boat.seo.title"] = Rich("<p>x</p>") });

        var html = await Run(content);

        Assert.StartsWith("<title>Boat · Saronida Sailing</title>", html);
    }

    private static readonly Guid SeoImageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static PageContent BoatImageContent(string locale, PageImage? photo, Dictionary<string, ContentValue>? extra = null)
    {
        var fields = new Dictionary<string, ContentValue> { ["boat.seo.image"] = Image(SeoImageId) };
        foreach (var (key, value) in extra ?? [])
        {
            fields[key] = value;
        }

        return new PageContent(
            "boat",
            locale,
            fields,
            new Dictionary<string, IReadOnlyList<ContentListEntry>>(),
            new Dictionary<string, bool>(),
            photo is null ? new Dictionary<Guid, PageImage>() : new Dictionary<Guid, PageImage> { [SeoImageId] = photo });
    }

    [Fact]
    public async Task SeoImage_EmitsAbsoluteUrl_WidthHeightAndAlt()
    {
        var content = BoatImageContent(
            "el", Photo(SeoImageId, 3000, 2000), new() { ["boat.seo.image-alt"] = Text("Το σκάφος \"Αιγαίο\"") });

        var html = await Run(content);

        Assert.EndsWith(
            "\n    <meta property=\"og:image\" content=\"https://localhost:5001/media/photo-1600.webp\" />"
            + "\n    <meta property=\"og:image:width\" content=\"1600\" />"
            + "\n    <meta property=\"og:image:height\" content=\"1067\" />"
            + "\n    <meta property=\"og:image:alt\" content=\"Το σκάφος &quot;Αιγαίο&quot;\" />",
            html);
    }

    [Theory]
    [InlineData(new[] { 480, 960, 1600, 2400 }, 4000, 3000, 1600, 1200)]
    [InlineData(new[] { 480, 960, 1600, 2400 }, 2400, 1000, 1600, 667)]
    [InlineData(new[] { 480, 960 }, 1000, 500, 960, 480)]
    [InlineData(new[] { 300 }, 300, 200, 300, 200)]
    public async Task SeoImage_PicksTheLargestSourceUpTo1600(int[] widths, int width, int height, int expectedWidth, int expectedHeight)
    {
        var content = BoatImageContent("el", Photo(SeoImageId, width, height, widths));

        var html = await Run(content);

        Assert.Contains($"/media/photo-{expectedWidth}.webp\"", html);
        Assert.Contains($"<meta property=\"og:image:width\" content=\"{expectedWidth}\" />", html);
        Assert.Contains($"<meta property=\"og:image:height\" content=\"{expectedHeight}\" />", html);
    }

    [Fact]
    public async Task SeoImage_OnlyLargerThan1600_UsesTheSmallest()
    {
        var content = BoatImageContent("el", Photo(SeoImageId, 4000, 2000, 1800, 2400));

        var html = await Run(content);

        Assert.Contains("/media/photo-1800.webp\"", html);
        Assert.Contains("og:image:height\" content=\"900\"", html);
    }

    [Fact]
    public async Task SeoImage_WithoutAlt_OmitsAltTag_AndEnglishFallsBackToGreekAlt()
    {
        var withoutAlt = await Run(BoatImageContent("el", Photo(SeoImageId)));
        var fallback = await Run(BoatImageContent(
            "en", Photo(SeoImageId), new() { ["boat.seo.image-alt"] = new ContentValue("Ελληνικό", ContentType.Text, true) }));

        Assert.DoesNotContain("og:image:alt", withoutAlt);
        Assert.Contains("<meta property=\"og:image:alt\" content=\"Ελληνικό\" />", fallback);
    }

    [Fact]
    public async Task SeoImage_MissingEmptyOrUnknown_EmitsNoImageTags()
    {
        var unknown = await Run(BoatImageContent("el", null));
        var empty = await Run(BoatContent("el", new() { ["boat.seo.image"] = new ContentValue("", ContentType.Image, false) }));
        var none = await Run(BoatContent("el", []));
        var wrongType = await Run(BoatContent("el", new() { ["boat.seo.image"] = Text("x") }));

        foreach (var html in new[] { unknown, empty, none, wrongType })
        {
            Assert.DoesNotContain("og:image", html);
        }
    }

    [Fact]
    public async Task NonSitePage_IgnoresSeoImage()
    {
        var html = await Run(BoatImageContent("el", Photo(SeoImageId)), pagePath: "/Error");

        Assert.DoesNotContain("og:image", html);
    }

    [Fact]
    public async Task NonSitePage_EmitsTitleOnly()
    {
        var html = await Run(Content(), pagePath: "/Error", fallbackTitle: "Error · Saronida Sailing");

        Assert.Equal("<title>Error · Saronida Sailing</title>", html);
    }

    [Fact]
    public async Task Values_AreEncoded()
    {
        var content = BoatContent("el", new()
        {
            ["boat.seo.title"] = Text("<script>&\""),
            ["boat.seo.description"] = Text("a\"><b>"),
        });

        var html = await Run(content);

        Assert.Contains("<title>&lt;script&gt;&amp;&quot;</title>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("content=\"a\">", html);
    }
}

public sealed class SiteOptionsTests
{
    private static SiteOptions Resolve(string? baseUrl)
    {
        var values = new Dictionary<string, string?>();
        if (baseUrl is not null)
        {
            values["Site:BaseUrl"] = baseUrl;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSiteOptions(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SiteOptions>>().Value;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost:5001")]
    [InlineData("/relative")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com/path/")]
    [InlineData("https://example.com?x=1")]
    [InlineData("https://example.com/?x=1")]
    [InlineData("https://example.com/#frag")]
    [InlineData("ftp://example.com")]
    public void Invalid_FailsValidation(string? baseUrl) =>
        Assert.Throws<OptionsValidationException>(() => Resolve(baseUrl));

    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("https://example.com/", "https://example.com")]
    [InlineData("http://localhost:5000/", "http://localhost:5000")]
    public void Valid_IsNormalisedWithoutTrailingSlash(string baseUrl, string expected) =>
        Assert.Equal(expected, Resolve(baseUrl).BaseUrl);
}

[Collection(WebHostCollection.Name)]
public class PageSeoWebHostTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private const string GreekTitle = "Ιστιοπλοΐα στη Σαρωνίδα";
    private const string GreekDescription = "Ημερήσιες εκδρομές με ιστιοφόρο";

    private async Task Write(string key, string locale, string value)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IContentService>();
        var result = await service.ApplyChangesAsync(
            [new SetField(key, locale, ContentType.Text, value)], new ContentEditor("test@example.com", null));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task Greek_Home_RendersGreekLiterally_AndAbsoluteUrl()
    {
        await Write("home.seo.title", "el", GreekTitle);
        await Write("home.seo.description", "el", GreekDescription);

        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains($"<title>{GreekTitle}</title>", html);
        Assert.Contains($"<meta name=\"description\" content=\"{GreekDescription}\" />", html);
        Assert.Contains($"<meta property=\"og:title\" content=\"{GreekTitle}\" />", html);
        Assert.Contains($"<meta property=\"og:description\" content=\"{GreekDescription}\" />", html);
        Assert.Contains("<meta property=\"og:url\" content=\"https://localhost/\" />", html);
        Assert.Contains("<meta property=\"og:locale\" content=\"el_GR\" />", html);
        Assert.DoesNotContain("&#x", html);
    }

    [Fact]
    public async Task English_Home_FallsBackToGreekTitle_WithEnglishTags()
    {
        await Write("home.seo.title", "el", GreekTitle);

        var html = await factory.CreateClient().GetStringAsync("/en/");

        Assert.Contains($"<title>{GreekTitle}</title>", html);
        Assert.Contains("<meta property=\"og:url\" content=\"https://localhost/en/\" />", html);
        Assert.Contains("<meta property=\"og:locale\" content=\"en_GB\" />", html);
        Assert.Contains("<meta property=\"og:locale:alternate\" content=\"el_GR\" />", html);
    }

    [Fact]
    public async Task HtmlSensitiveCharacters_AreStillEscaped()
    {
        await Write("home.seo.title", "el", "<b>\"&");
        await Write("home.seo.description", "el", "x\"><i>");

        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("<title>&lt;b&gt;&quot;&amp;</title>", html);
        Assert.DoesNotContain("<b>\"&", html);
        Assert.DoesNotContain("<i>", html);
    }
}
