using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class ContactPageTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static readonly ContentEditor Editor = new("contact-test@example.com", null);

    private Task<string> GetAsync(string url) => NewClient(factory).GetStringAsync(url);

    private static IReadOnlyList<string> Tiles(string html) =>
        Regex.Matches(html, "<a class=\"contact-tile\"[^>]*>.*?</a>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .ToList();

    private static string Tag(string html, string attribute)
    {
        var match = Regex.Match(html, $"<[a-z]+ [^>]*{Regex.Escape(attribute)}[^>]*>");
        Assert.True(match.Success, $"No element with {attribute}.");
        return match.Value;
    }

    private static bool IsHidden(string tag) => Regex.IsMatch(tag, @"\shidden(\s|=|>)");

    public static TheoryData<string, string[]> SeededText() => new()
    {
        {
            "/epikoinonia",
            [
                "<html lang=\"el\">",
                "Επικοινωνία · Saronida Sailing",
                "Μιλήστε μας απευθείας",
                "Χωρίς φόρμες και αναμονή.",
                "Καλέστε μας",
                "Δεύτερο τηλέφωνο",
                "Στείλτε email",
                "Μήνυμα στο Instagram",
                "@saronidasailing",
                "Πού θα μας βρείτε",
                "Λιμάνι Αγίου Νικολάου",
                "Οδηγίες στον χάρτη",
                "Με αυτοκίνητο",
                "Μεταφορά",
                "Σημείο συνάντησης",
                "Ο χάρτης της Google φορτώνει μόνο με τη συναίνεσή σας.",
                "Χρήσιμα στοιχεία",
                "Ώρες επικοινωνίας",
                "[ΩΡΕΣ]",
                "[ΜΗΝΕΣ]",
                "Έτοιμοι για θάλασσα;",
                "Δείτε τις εκδρομές",
            ]
        },
        {
            "/en/contact",
            [
                "<html lang=\"en\">",
                "Contact · Saronida Sailing",
                "Talk to us directly",
                "No forms and no waiting.",
                "Call us",
                "Second phone",
                "Send an email",
                "Message on Instagram",
                "@saronidasailing",
                "Where to find us",
                "Agios Nikolaos harbour",
                "Directions on the map",
                "By car",
                "Transfer",
                "Meeting point",
                "The Google map loads only with your consent.",
                "Useful details",
                "Contact hours",
                "[HOURS]",
                "[MONTHS]",
                "Ready for the sea?",
                "See the trips",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(SeededText))]
    public async Task Contact_RendersEverySeededSection_InTheVisitorsLanguage(string url, string[] expected)
    {
        var html = await GetAsync(url);

        foreach (var text in expected)
        {
            Assert.Contains(text, html);
        }

        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Single(Regex.Matches(html, "<main[ >]"));
        Assert.DoesNotContain("data-cms", html);
    }

    [Theory]
    [InlineData("/epikoinonia", "/ekdromes")]
    [InlineData("/en/contact", "/en/trips")]
    public async Task Cta_LinksToTrips(string url, string tripsPath)
    {
        var html = await GetAsync(url);

        Assert.Contains($"<a class=\"btn btn--cta contact-cta__button\" href=\"{tripsPath}\">", html);
    }

    [Theory]
    [InlineData("/epikoinonia")]
    [InlineData("/en/contact")]
    public async Task Tiles_AreFourLinks_WithTheRightTargets(string url)
    {
        var tiles = Tiles(await GetAsync(url));

        Assert.Equal(4, tiles.Count);
        Assert.Matches(@"href=""tel:(\+|&#x2B;)306940057799""", tiles[0]);
        Assert.Matches(@"(\+|&#x2B;)30 694 005 7799</strong>", tiles[0]);
        Assert.Matches(@"href=""tel:(\+|&#x2B;)306978856418""", tiles[1]);
        Assert.Matches(@"(\+|&#x2B;)30 697 885 6418</strong>", tiles[1]);
        Assert.Contains("href=\"mailto:email@example.com\"", tiles[2]);
        Assert.Contains("[EMAIL]</strong>", tiles[2]);
        Assert.Contains("href=\"https://ig.me/m/saronidasailing\"", tiles[3]);
        Assert.Contains("@saronidasailing</strong>", tiles[3]);
        Assert.All(tiles, tile => Assert.Contains("<span class=\"contact-tile__label\">", tile));
    }

    [Fact]
    public async Task DirectionsLink_OpensTheSeededMapsAddress_InANewTab()
    {
        var html = await GetAsync("/epikoinonia");

        var link = Regex.Match(html, "<a class=\"btn btn--dark contact-where__directions\"[^>]*>").Value;
        Assert.Contains("href=\"https://maps.app.goo.gl/UPT73MuDqkViH82M9\"", link);
        Assert.Contains("target=\"_blank\"", link);
    }

    [Fact]
    public async Task Details_AreAListOfFour()
    {
        var html = await GetAsync("/epikoinonia");

        var items = Regex.Matches(html, "<li class=\"contact-detail\">.*?</li>", RegexOptions.Singleline);
        Assert.Equal(4, items.Count);
        Assert.Contains("Γλώσσες", items[0].Value);
        Assert.Contains("Ελληνικά, English", items[0].Value);
        Assert.Contains("Άγιος Νικόλαος", items[3].Value);
        Assert.Equal(3, Regex.Matches(html, "<li class=\"contact-way\">").Count);
    }

    [Theory]
    [InlineData("/epikoinonia")]
    [InlineData("/en/contact")]
    public async Task Contact_NeverHasAnIframeInTheServerHtml(string url)
    {
        Assert.DoesNotContain("<iframe", await GetAsync(url), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbedUrl_IsHiddenForVisitors_AndNoIframeEvenWhenSet()
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        const string url = "https://www.google.com/maps/embed?pb=!1m18!1m12";
        Assert.True((await content.ApplyChangesAsync(
            [new SetField(ContactMap.EmbedUrlKey, "el", ContentType.Text, url)], Editor)).Succeeded);
        try
        {
            foreach (var path in new[] { "/epikoinonia", "/en/contact" })
            {
                var html = await GetAsync(path);

                var tag = Tag(html, "data-map-url");
                Assert.True(IsHidden(tag), tag);
                Assert.Contains($">{url}</p>", html);
                Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync(
                [new SetField(ContactMap.EmbedUrlKey, "el", ContentType.Text, "")], Editor)).Succeeded);
        }
    }

    [Fact]
    public async Task MapButton_StartsHidden_UntilScriptRuns()
    {
        var html = await GetAsync("/epikoinonia");

        Assert.True(IsHidden(Tag(html, "data-map-settings")));
        Assert.Contains("Ρυθμίσεις cookies</button>", html);
    }

    public static TheoryData<string, string, string> ConsentPages() => new()
    {
        { "/", "/aporrito", "Πολιτική απορρήτου" },
        { "/ekdromes", "/aporrito", "Πολιτική απορρήτου" },
        { "/epikoinonia", "/aporrito", "Πολιτική απορρήτου" },
        { "/oroi", "/aporrito", "Πολιτική απορρήτου" },
        { "/en/", "/en/privacy", "Privacy policy" },
        { "/en/contact", "/en/privacy", "Privacy policy" },
        { "/en/terms", "/en/privacy", "Privacy policy" },
    };

    [Theory]
    [MemberData(nameof(ConsentPages))]
    public async Task ConsentBanner_IsOnEveryPage_HiddenUntilScriptRuns(string url, string privacyPath, string privacyName)
    {
        var html = await GetAsync(url);

        var root = Tag(html, "data-consent ");
        Assert.True(IsHidden(root), root);
        Assert.Contains($"<a href=\"{privacyPath}\">{privacyName}</a>", html);
        Assert.Contains("role=\"dialog\" aria-modal=\"false\"", html);
        var switchTag = Tag(html, "data-consent-switch");
        Assert.Contains("role=\"switch\"", switchTag);
        Assert.Contains("aria-checked=\"false\"", switchTag);
        Assert.Contains("aria-labelledby=\"consent-external-title\"", switchTag);
        Assert.True(IsHidden(Tag(html, "data-consent-open")));
        Assert.True(IsHidden(Tag(html, "data-consent-view=\"settings\"")));
        Assert.True(html.IndexOf("data-consent ", StringComparison.Ordinal) < html.IndexOf("<header", StringComparison.Ordinal));
        Assert.True(html.IndexOf("class=\"skip-link\"", StringComparison.Ordinal) < html.IndexOf("data-consent ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/epikoinonia", new[] { "Cookies και απόρρητο", "Ο ιστότοπος λειτουργεί χωρίς cookies παρακολούθησης.", "Ρυθμίσεις cookies", "Απαραίτητα", "Εξωτερικό περιεχόμενο", "Πάντα ενεργά", "Απόρριψη όλων", "Αποθήκευση", "Αποδοχή όλων", "Επιλογή ανά κατηγορία" })]
    [InlineData("/en/", new[] { "Cookies and privacy", "This website runs without tracking cookies.", "Cookie settings", "Necessary", "External content", "Always on", "Reject all", "Accept all", "Choose by category" })]
    public async Task ConsentBanner_ShowsTheSeededTexts(string url, string[] expected)
    {
        var html = await GetAsync(url);

        foreach (var text in expected)
        {
            Assert.Contains(text, html);
        }

        Assert.DoesNotContain("Στατιστικά", html);
    }

    [Fact]
    public async Task ConsentBanner_RejectAndAccept_HaveTheSameStyle()
    {
        var html = await GetAsync("/epikoinonia");

        var reject = Tag(html, "data-consent-action=\"reject\"");
        var accept = Tag(html, "data-consent-action=\"accept\"");
        Assert.Equal(
            Regex.Match(reject, "class=\"[^\"]*\"").Value,
            Regex.Match(accept, "class=\"[^\"]*\"").Value);
    }

    [Fact]
    public async Task Contact_SetsNoCookie_ForVisitors()
    {
        using var response = await NewClient(factory).GetAsync("/epikoinonia");

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public void SharedContactSeed_Version2_AddsOnlyTheEmail()
    {
        var group = new EmbeddedContentSeedSource().GetGroups().Single(g => g.Name == "shared.contact");

        Assert.Equal(2, group.Version);
        Assert.Equal(
            ["shared.contact.address", "shared.contact.email", "shared.contact.phone", "shared.contact.phone-2"],
            group.Fields.Select(f => f.Key).Order());
        var email = group.Fields.Single(f => f.Key == "shared.contact.email");
        Assert.Equal(ContentType.Link, email.Type);
        Assert.Contains("[EMAIL]", email.Values["el"]);
        Assert.Contains("mailto:email@example.com", email.Values["en"]);
    }

    [Fact]
    public void ContactAndCookiesSeeds_Load_WithTheirKeys()
    {
        var groups = new EmbeddedContentSeedSource().GetGroups();

        var contact = groups.Single(g => g.Name == "contact");
        Assert.DoesNotContain(contact.Fields, f => f.Key == ContactMap.EmbedUrlKey);
        Assert.Contains(contact.Fields, f => f.Key == "contact.map.image-alt");
        Assert.Contains(contact.Fields, f => f.Key == "contact.where.directions" && f.Type == ContentType.Link);
        Assert.Equal(["contact.details-items", "contact.where-items"], contact.Lists.Select(l => l.ListKey).Order());

        var cookies = groups.Single(g => g.Name == "shared.cookies");
        Assert.Equal(1, cookies.Version);
        Assert.Equal(
            [
                "shared.cookies.external-text", "shared.cookies.external-title", "shared.cookies.necessary-text",
                "shared.cookies.necessary-title", "shared.cookies.settings-title", "shared.cookies.text",
                "shared.cookies.title",
            ],
            cookies.Fields.Select(f => f.Key).Order());
    }
}
