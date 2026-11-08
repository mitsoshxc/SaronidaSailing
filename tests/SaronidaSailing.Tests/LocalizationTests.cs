using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Tests;

public class LocalizationHttpTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task Root_IsGreek()
    {
        var response = await factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"el\"", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/en/")]
    [InlineData("/en")]
    [InlineData("/EN/")]
    public async Task EnglishHome_IsEnglish(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"en\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IndexPath_IsNotRoutable()
    {
        var response = await factory.CreateClient().GetAsync("/Index");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class LocalizedUrlServiceTests
{
    private static LocalizedUrlService CreateService(HttpContext? context = null) =>
        new(new HttpContextAccessor { HttpContext = context });

    public static TheoryData<string, string, string> Paths => new()
    {
        { "home", "el", "/" },
        { "home", "en", "/en/" },
        { "trips", "el", "/ekdromes" },
        { "trips", "en", "/en/trips" },
        { "boat", "el", "/skafos" },
        { "boat", "en", "/en/boat" },
        { "contact", "el", "/epikoinonia" },
        { "contact", "en", "/en/contact" },
        { "terms", "el", "/oroi" },
        { "terms", "en", "/en/terms" },
        { "privacy", "el", "/aporrito" },
        { "privacy", "en", "/en/privacy" },
        { "accessibility", "el", "/prosvasimotita" },
        { "accessibility", "en", "/en/accessibility" },
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public void GetPath_ReturnsExpectedPath(string key, string culture, string expected) =>
        Assert.Equal(expected, CreateService().GetPath(key, culture));

    [Fact]
    public void GetPath_UnknownKey_Throws() =>
        Assert.Throws<ArgumentException>(() => CreateService().GetPath("nope", "el"));

    [Fact]
    public void GetPath_UnknownCulture_Throws() =>
        Assert.Throws<ArgumentException>(() => CreateService().GetPath("home", "fr"));

    [Theory]
    [InlineData("el", "/en/")]
    [InlineData("en", "/")]
    public void GetAlternatePath_Home_ReturnsOtherLanguage(string culture, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["page"] = "/Index";
        context.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(new RequestCulture(new CultureInfo(culture)), null));

        Assert.Equal(expected, CreateService(context).GetAlternatePath());
    }

    [Fact]
    public void GetAlternatePath_NonSitePage_ReturnsNull()
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["page"] = "/Error";

        Assert.Null(CreateService(context).GetAlternatePath());
    }
}

public class UrlPrefixRequestCultureProviderTests
{
    [Theory]
    [InlineData("/", "el")]
    [InlineData("/skafos", "el")]
    [InlineData("/en", "en")]
    [InlineData("/en/", "en")]
    [InlineData("/en/boat", "en")]
    [InlineData("/EN/boat", "en")]
    [InlineData("/english", "el")]
    [InlineData("/enx", "el")]
    public async Task DeterminesCultureFromPath(string path, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        var result = await new UrlPrefixRequestCultureProvider().DetermineProviderCultureResult(context);

        Assert.Equal(expected, Assert.Single(result!.Cultures).Value);
        Assert.Equal(expected, Assert.Single(result.UICultures).Value);
    }
}
