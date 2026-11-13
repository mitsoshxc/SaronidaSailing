using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Interfaces;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class HomePageContentTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static string Slice(string html, string startMarker, string endMarker)
    {
        var start = html.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{startMarker}' not found.");
        var end = html.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"'{endMarker}' not found after '{startMarker}'.");
        return html[start..end];
    }

    private static string Strong(string fragment) => Regex.Match(fragment, "<strong[^>]*>([^<]*)</strong>").Groups[1].Value;

    private Task<string> GetAsync(string url) => NewClient(factory).GetStringAsync(url);

    public static TheoryData<string, string[]> SeededText() => new()
    {
        {
            "/",
            [
                "Καταμαράν με σκίπερ · Αθηναϊκή Ριβιέρα",
                "Η Αθηναϊκή Ριβιέρα με καταμαράν Lagoon 500",
                "Ημερήσιες κρουαζιέρες στο Σούνιο, εκδρομές στα νησιά του Σαρωνικού",
                "Κλείστε ημερήσια κρουαζιέρα",
                "Γνωρίστε το σκάφος",
                "Με μια ματιά",
                "15,5 μ.",
                "επιβάτες ανά εκδρομή",
                "Πόσο θέλετε να μείνετε στη θάλασσα;",
                "Ημερήσια κρουαζιέρα στο Σούνιο",
                "Γύρος στα νησιά",
                "Εβδομαδιαία ναύλωση με σκίπερ",
                "Lagoon 500: χώρος για όλους",
                "Έτος κατασκευής",
                "Εσείς χαλαρώνετε. Εμείς πλέουμε.",
                "Τοπική γνώση",
                "Αναχωρούμε από το λιμάνι του <strong>Αγίου Νικολάου, Ανάβυσσος</strong>",
                "Πώς θα έρθετε",
                "Ημερήσια κρουαζιέρα από",
                "Λεπτομέρειες",
                "Ημερομηνίες",
                "Όλες οι εκδρομές",
                "Σχέδιο καταμαράν Lagoon με πανιά στο ηλιοβασίλεμα",
                "[ΤΙΜΗ] / εβδ.",
                "<html lang=\"el\">",
            ]
        },
        {
            "/en/",
            [
                "Catamaran with skipper · Athens Riviera",
                "The Athens Riviera on a Lagoon 500 catamaran",
                "Day cruises to Sounion, trips to the Saronic islands",
                "Book a day cruise",
                "Discover the boat",
                "At a glance",
                "15.5 m",
                "passengers per trip",
                "How long do you want to stay at sea?",
                "Day cruise to Sounion",
                "Island tour",
                "Weekly charter with skipper",
                "Lagoon 500: room for everyone",
                "Year built",
                "You relax. We sail.",
                "Local knowledge",
                "We depart from the harbour of <strong>Agios Nikolaos, Anavyssos</strong>",
                "How to get here",
                "Day cruise from",
                "Details",
                "Dates",
                "All trips",
                "Drawing of a Lagoon catamaran under sail at sunset",
                "[PRICE] / week",
                "<html lang=\"en\">",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(SeededText))]
    public async Task Home_RendersEverySeededSection_InTheVisitorsLanguage(string url, string[] expected)
    {
        var html = await GetAsync(url);

        foreach (var text in expected)
        {
            Assert.Contains(text, html);
        }

        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.DoesNotContain("data-cms", html);
    }

    [Fact]
    public async Task Home_Greek_HasNoEnglishText()
    {
        var html = await GetAsync("/");

        Assert.DoesNotContain("Book a day cruise", html);
        Assert.DoesNotContain("At a glance", html);
    }

    [Fact]
    public async Task Home_Greek_LinksToTheTripsAndTheBoat()
    {
        var html = await GetAsync("/");

        var hero = Slice(html, "class=\"section section--dark hero\"", "</section>");
        Assert.Contains("href=\"/ekdromes?trip=day\"", hero);
        Assert.Contains("href=\"/skafos\"", hero);

        var trips = Slice(html, "class=\"section trips\"", "</section>");
        Assert.Contains("href=\"/ekdromes?trip=day\"", trips);
        Assert.Contains("href=\"/ekdromes?trip=islands\"", trips);
        Assert.Contains("href=\"/ekdromes?trip=week\"", trips);
        Assert.Contains("href=\"/ekdromes\"", trips);
        Assert.Equal(3, Regex.Matches(trips, "<article class=\"trip-card\"").Count);

        Assert.Contains("href=\"/skafos\"", Slice(html, "class=\"section section--dark boat\"", "</section>"));
        Assert.Contains("href=\"/epikoinonia\"", Slice(html, "class=\"departure\"", "</section>"));
        Assert.Contains("href=\"/ekdromes?trip=day\"", Slice(html, "class=\"sticky-book\"", "</main>"));
    }

    [Fact]
    public async Task Home_English_LinksToTheEnglishPages()
    {
        var html = await GetAsync("/en/");

        var trips = Slice(html, "class=\"section trips\"", "</section>");
        Assert.Contains("href=\"/en/trips?trip=day\"", trips);
        Assert.Contains("href=\"/en/trips?trip=islands\"", trips);
        Assert.Contains("href=\"/en/trips?trip=week\"", trips);
        Assert.DoesNotContain("href=\"/ekdromes", trips);
        Assert.Contains("href=\"/en/trips?trip=day\"", Slice(html, "class=\"section section--dark hero\"", "</section>"));
        Assert.Contains("href=\"/en/boat\"", Slice(html, "class=\"section section--dark boat\"", "</section>"));
        Assert.Contains("href=\"/en/contact\"", Slice(html, "class=\"departure\"", "</section>"));
    }

    [Theory]
    [InlineData("/", "[ΤΙΜΗ]")]
    [InlineData("/en/", "[PRICE]")]
    public async Task Home_StickyBar_ShowsTheSamePriceAsTheDayCard(string url, string price)
    {
        var html = await GetAsync(url);

        var dayCard = Slice(html, "<article class=\"trip-card\"", "</article>");
        var sticky = Slice(html, "class=\"sticky-book\"", "</main>");

        Assert.Equal(price, Strong(dayCard));
        Assert.Equal(Strong(dayCard), Strong(sticky));
    }

    [Fact]
    public async Task Home_Lists_RenderTheSeededItems()
    {
        var html = await GetAsync("/");

        Assert.Equal(4, Regex.Matches(Slice(html, "class=\"stat-grid glance__grid\"", "</ul>"), "<li class=\"stat\"").Count);
        Assert.Equal(4, Regex.Matches(Slice(html, "class=\"stat-grid boat__specs\"", "</ul>"), "<li class=\"stat\"").Count);
        Assert.Equal(6, Regex.Matches(Slice(html, "class=\"why-grid\"", "</ul>"), "<li class=\"why-item\"").Count);
    }

    [Fact]
    public async Task HidingTheIslandsCard_RemovesItForVisitors()
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        var editor = new ContentEditor("home-test@example.com", null);
        Assert.True((await content.ApplyChangesAsync([new SetSectionActive("home.trip-islands", false)], editor)).Succeeded);
        try
        {
            var html = await GetAsync("/");

            Assert.DoesNotContain("Γύρος στα νησιά", html);
            Assert.DoesNotContain("trip=islands", html);
            Assert.Contains("Ημερήσια κρουαζιέρα στο Σούνιο", html);
            Assert.Contains("Εβδομαδιαία ναύλωση με σκίπερ", html);
            Assert.Equal(2, Regex.Matches(html, "<article class=\"trip-card\"").Count);
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync([new SetSectionActive("home.trip-islands", true)], editor)).Succeeded);
        }

        Assert.Contains("Γύρος στα νησιά", await GetAsync("/"));
    }

    [Fact]
    public async Task Admin_SeesTheInstagramSectionWithItsSeededTitleAndText()
    {
        var admin = NewClient(factory);
        await LoginWithTotpAsync(admin, await AddTwoFactorAdminAsync(factory, "home-admin@example.com"));

        var html = await admin.GetStringAsync("/");

        var instagram = Slice(html, "home.instagram\"", "</section>");
        Assert.Contains("@saronidasailing", instagram);
        Assert.Contains("Στιγμές από τις εκδρομές μας στο Instagram", instagram);
        Assert.Contains("data-cms-key=\"home.instagram.text\"", instagram);
        Assert.Contains("No Instagram posts yet.", instagram);
    }
}
