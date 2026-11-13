using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Tests;

public class UiTextTests
{
    [Fact]
    public void EveryString_HasBothLanguages()
    {
        foreach (var (name, text) in UiText.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(text.Greek), $"{name} has no Greek text.");
            Assert.False(string.IsNullOrWhiteSpace(text.English), $"{name} has no English text.");
        }
    }

    [Fact]
    public void All_ListsEveryUiString()
    {
        var declared = typeof(UiText)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(UiString))
            .Select(p => p.Name)
            .Order();

        Assert.Equal(declared, UiText.All.Select(a => a.Name).Order());
    }

    [Fact]
    public void For_PicksTheLocale()
    {
        Assert.Equal("The boat", UiText.Boat.For("en"));
        Assert.Equal("Το σκάφος", UiText.Boat.For("el"));
    }

    [Fact]
    public void EverySitePage_HasAMenuNameAndATitle()
    {
        foreach (var page in SitePages.All)
        {
            Assert.NotNull(UiText.MenuName(page.Key));
            Assert.NotNull(UiText.PageTitle(page.Key));
        }

        Assert.Equal("Εκδρομές", UiText.MenuName("trips").Greek);
        Assert.Equal("Εκδρομές & κράτηση", UiText.PageTitle("trips").Greek);
        Assert.Equal("The boat", UiText.PageTitle("boat").English);
    }

    [Fact]
    public void LanguageLink_PointsToTheOtherLanguage()
    {
        Assert.Equal(new LanguageLinkText("EN", "en", "English version"), UiText.LanguageLink("el"));
        Assert.Equal(new LanguageLinkText("ΕΛ", "el", "Ελληνική έκδοση"), UiText.LanguageLink("en"));
    }
}

[Collection(WebHostCollection.Name)]
public class SiteRoutesTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/ekdromes")]
    [InlineData("/skafos")]
    [InlineData("/epikoinonia")]
    [InlineData("/oroi")]
    [InlineData("/aporrito")]
    [InlineData("/prosvasimotita")]
    [InlineData("/en/")]
    [InlineData("/en/trips")]
    [InlineData("/en/boat")]
    [InlineData("/en/contact")]
    [InlineData("/en/terms")]
    [InlineData("/en/privacy")]
    [InlineData("/en/accessibility")]
    public async Task EveryPageRoute_ReturnsOk(string path)
    {
        var response = await AdminTestHelpers.NewClient(factory).GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/en/skafos")]
    [InlineData("/boat")]
    [InlineData("/en/ekdromes")]
    [InlineData("/trips")]
    public async Task CrossLanguageSlugs_ReturnNotFound(string path)
    {
        var response = await AdminTestHelpers.NewClient(factory).GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Layout_HasSkipLinkFirst_MainTarget_AndScript()
    {
        var html = await AdminTestHelpers.NewClient(factory).GetStringAsync("/ekdromes");

        Assert.Contains("<html lang=\"el\"", html);
        Assert.True(
            html.IndexOf("class=\"skip-link\" href=\"#main\"", StringComparison.Ordinal)
                < html.IndexOf("<header", StringComparison.Ordinal));
        Assert.Contains("<main id=\"main\" tabindex=\"-1\">", html);
        Assert.Matches(@"<script type=""module"" src=""/js/site[^""]*\.js""></script>", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://localhost/ekdromes\" />", html);
        Assert.Contains("hreflang=\"x-default\" href=\"https://localhost/ekdromes\"", html);
        Assert.DoesNotContain("cms-edit.js", html);
    }

    [Fact]
    public async Task Layout_SvgIdsAreUnique_AcrossHeaderAndFooter()
    {
        var html = await AdminTestHelpers.NewClient(factory).GetStringAsync("/");

        var ids = Regex.Matches(html, "<path id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(4, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}

[Collection(WebHostCollection.Name)]
public class SiteHeaderTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private Task<string> Get(string path) => AdminTestHelpers.NewClient(factory).GetStringAsync(path);

    [Fact]
    public async Task Greek_Trips_MarksCurrentNavLink_AndBookPointsToTrips()
    {
        var html = await Get("/ekdromes");

        Assert.Contains("<a class=\"site-nav__link\" href=\"/ekdromes\" aria-current=\"page\">Εκδρομές</a>", html);
        Assert.Contains("<a class=\"site-nav__link\" href=\"/skafos\">Το σκάφος</a>", html);
        Assert.Contains("<a class=\"btn btn--cta site-menu__book\" href=\"/ekdromes\">Κράτηση</a>", html);
        Assert.Contains("aria-label=\"Κύρια πλοήγηση\"", html);
        Assert.Contains("class=\"site-header__back\" href=\"/\" aria-label=\"Πίσω στην αρχική\"", html);
        Assert.Contains("<span class=\"site-header__title\">Εκδρομές &amp; κράτηση</span>", html);
        Assert.Contains("class=\"site-header site-header--inner\"", html);
    }

    [Fact]
    public async Task English_Boat_UsesEnglishPaths_AndLanguageLinkGoesToGreek()
    {
        var html = await Get("/en/boat");

        Assert.Contains("<a class=\"site-nav__link\" href=\"/en/trips\">Trips</a>", html);
        Assert.Contains("<a class=\"site-nav__link\" href=\"/en/boat\" aria-current=\"page\">The boat</a>", html);
        Assert.Contains("<a class=\"site-nav__link\" href=\"/en/contact\">Contact</a>", html);
        Assert.Contains("<a class=\"btn btn--cta site-menu__book\" href=\"/en/trips\">Book</a>", html);
        Assert.Contains(
            "<a class=\"site-lang site-lang--menu\" href=\"/skafos\" hreflang=\"el\" lang=\"el\" aria-label=\"Ελληνική έκδοση\">ΕΛ</a>",
            html);
        Assert.Contains("aria-label=\"Back to home\"", html);
    }

    [Fact]
    public async Task Greek_Home_LanguageLinkGoesToEnglishHome_AndIsNotAnInnerPage()
    {
        var html = await Get("/");

        Assert.Contains(
            "<a class=\"site-lang site-lang--bar\" href=\"/en/\" hreflang=\"en\" lang=\"en\" aria-label=\"English version\">EN</a>",
            html);
        Assert.DoesNotContain("site-header--inner", html);
        Assert.DoesNotContain("site-header__back", html);
        Assert.DoesNotMatch("class=\"site-nav__link\"[^>]*aria-current", html);
    }

    [Fact]
    public async Task MenuButton_IsWiredForTheScript()
    {
        var html = await Get("/");

        Assert.Contains("aria-expanded=\"false\" aria-controls=\"site-menu\" aria-label=\"Άνοιγμα μενού\"", html);
        Assert.Contains("data-label-close=\"Κλείσιμο μενού\"", html);
        Assert.Contains("id=\"site-menu\"", html);
        Assert.Contains("aria-label=\"Επιστροφή στην αρχή της σελίδας\"", html);
    }

    [Fact]
    public async Task Logo_HasAccessibleName_AndHiddenSvg()
    {
        var html = await Get("/en/");

        Assert.Contains("href=\"/en/\" aria-label=\"Saronida Sailing, home page\"", html);
        Assert.Contains("viewBox=\"0 0 100 100\" aria-hidden=\"true\"", html);
    }
}

[Collection(WebHostCollection.Name)]
public class SiteFooterTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private static string Footer(string html)
    {
        var start = html.IndexOf("<footer class=\"site-footer\">", StringComparison.Ordinal);
        var end = html.IndexOf("</footer>", start, StringComparison.Ordinal);
        return html[start..(end + "</footer>".Length)];
    }

    private async Task Save(string key, ContentType type, string value)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IContentService>().ApplyChangesAsync(
            [new SetField(key, "el", type, value)], new ContentEditor("test@example.com", null));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task Footer_Visitor_HidesEmptyContent_ThenShowsSavedLinks()
    {
        var client = AdminTestHelpers.NewClient(factory);

        var empty = Footer(await client.GetStringAsync("/epikoinonia"));

        Assert.DoesNotContain("site-footer__social-link", empty);
        Assert.DoesNotContain("href=\"tel:", empty);
        Assert.DoesNotMatch("<a[^>]*></a>", empty);
        Assert.Contains("<a href=\"/ekdromes\">Εκδρομές &amp; κράτηση</a>", empty);
        Assert.Contains("<a href=\"/epikoinonia\" aria-current=\"page\">Επικοινωνία</a>", empty);
        Assert.Contains("<a href=\"/prosvasimotita\">Προσβασιμότητα</a>", empty);
        Assert.DoesNotContain("Cookie", empty);

        await Save(
            "shared.social.instagram",
            ContentType.Link,
            "{\"url\":\"https://www.instagram.com/saronidasailing/\",\"label\":\"Η Saronida Sailing στο Instagram\"}");
        await Save("shared.contact.phone", ContentType.Link, "{\"url\":\"tel:+306940057799\",\"label\":\"+30 694 005 7799\"}");
        await Save("shared.footer.copyright", ContentType.Text, "© 2026 Test");

        var html = await client.GetStringAsync("/epikoinonia");
        var footer = Footer(html);

        Assert.Matches(
            "<a class=\"site-footer__social-link\" href=\"https://www\\.instagram\\.com/saronidasailing/\" target=\"_blank\" rel=\"noopener\"><svg[^>]*aria-hidden=\"true\"[^>]*>.*?</svg><span class=\"visually-hidden\">Η Saronida Sailing στο Instagram</span>",
            footer);
        Assert.DoesNotContain("href=\"https://www.facebook", footer);
        Assert.Matches(@"<a href=""tel:(\+|&#x2B;)306940057799"">(\+|&#x2B;)30 694 005 7799</a>", footer);
        Assert.Contains("<p>© 2026 Test</p>", footer);
        Assert.DoesNotContain("cms-", html);
        Assert.DoesNotContain("data-cms-", html);
        Assert.Matches(
            "<a class=\"site-nav__link\" href=\"https://www\\.instagram\\.com/saronidasailing/\" target=\"_blank\" rel=\"noopener\">Instagram",
            html);
    }
}
