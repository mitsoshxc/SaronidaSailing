using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class TripsPageTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static readonly ContentEditor Editor = new("trips-test@example.com", null);

    private Task<string> GetAsync(string url) => NewClient(factory).GetStringAsync(url);

    private static string Tag(string html, string attribute)
    {
        var match = Regex.Match(html, $"<[a-z]+ [^>]*{Regex.Escape(attribute)}[^>]*>");
        Assert.True(match.Success, $"No element with {attribute}.");
        return match.Value;
    }

    private static bool IsHidden(string tag) => Regex.IsMatch(tag, @"\shidden(\s|=|>)");

    private static string Panel(string html, string id) => Tag(html, $"data-trip-panel=\"{id}\"");

    private static string TabLink(string html, string id) => Tag(html, $"data-trip-tab=\"{id}\"");

    private async Task WithSection(string key, bool active, Func<Task> body)
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        Assert.True((await content.ApplyChangesAsync([new SetSectionActive(key, active)], Editor)).Succeeded);
        try
        {
            await body();
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync([new SetSectionActive(key, !active)], Editor)).Succeeded);
        }
    }

    [Fact]
    public async Task Default_ShowsTheDayPanel_AndHidesTheOthers()
    {
        var html = await GetAsync("/ekdromes");

        Assert.False(IsHidden(Panel(html, "day")));
        Assert.True(IsHidden(Panel(html, "islands")));
        Assert.True(IsHidden(Panel(html, "week")));
        Assert.Contains("aria-current=\"page\"", TabLink(html, "day"));
        Assert.DoesNotContain("aria-current", TabLink(html, "islands"));
        Assert.DoesNotContain("aria-current", TabLink(html, "week"));
    }

    [Fact]
    public async Task TripQuery_SelectsThatTrip_AndMarksItsLinkCurrent()
    {
        var html = await GetAsync("/ekdromes?trip=islands");

        Assert.True(IsHidden(Panel(html, "day")));
        Assert.False(IsHidden(Panel(html, "islands")));
        Assert.True(IsHidden(Panel(html, "week")));
        Assert.Contains("aria-current=\"page\"", TabLink(html, "islands"));
        Assert.DoesNotContain("aria-current", TabLink(html, "day"));
    }

    [Theory]
    [InlineData("?trip=ISLANDS", "islands")]
    [InlineData("?trip=bogus", "day")]
    [InlineData("?trip=", "day")]
    [InlineData("?trip=week&trip=islands", "day")]
    public async Task TripQuery_IsCaseInsensitive_AndUnknownValuesMeanDay(string query, string selected)
    {
        var html = await GetAsync("/ekdromes" + query);

        foreach (var id in new[] { "day", "islands", "week" })
        {
            Assert.Equal(id != selected, IsHidden(Panel(html, id)));
        }
    }

    [Fact]
    public async Task English_Week_RendersTheEnglishWeekPanel()
    {
        var html = await GetAsync("/en/trips?trip=week");

        Assert.Contains("<html lang=\"en\">", html);
        Assert.False(IsHidden(Panel(html, "week")));
        Assert.True(IsHidden(Panel(html, "day")));
        Assert.Contains("Weekly charter with skipper", html);
        Assert.Contains("href=\"/en/trips?trip=islands\"", TabLink(html, "islands"));
        Assert.Contains("Boat and skipper, per week", html);
        Assert.Contains("[PRICE] / week", html);
    }

    [Fact]
    public async Task TabLinks_PointToThePageInTheCurrentLanguage()
    {
        var greek = await GetAsync("/ekdromes");
        var english = await GetAsync("/en/trips");

        Assert.Contains("href=\"/ekdromes?trip=day\"", TabLink(greek, "day"));
        Assert.Contains("href=\"/ekdromes?trip=week\"", TabLink(greek, "week"));
        Assert.Contains("href=\"/en/trips?trip=week\"", TabLink(english, "week"));
    }

    [Fact]
    public async Task TabLabels_HaveALongAndAShortForm_AndOnlyTheLongOneIsExposed()
    {
        var html = await GetAsync("/ekdromes");

        Assert.Contains("<span class=\"trip-tab__long\">Εβδομαδιαία ναύλωση</span><span class=\"trip-tab__short\" aria-hidden=\"true\">Εβδομαδιαία</span>", html);
        Assert.Contains("aria-label=\"Τύπος εκδρομής\"", html);
    }

    [Theory]
    [InlineData("/ekdromes?trip=islands", "/ekdromes")]
    [InlineData("/en/trips?trip=week", "/en/trips")]
    public async Task Canonical_IgnoresTheTripQuery(string url, string canonical)
    {
        var html = await GetAsync(url);

        Assert.Contains($"<link rel=\"canonical\" href=\"https://localhost{canonical}\" />", html);
    }

    [Fact]
    public async Task HidingTheIslands_RemovesItsTabAndPanel_AndTheQueryFallsBackToDay()
    {
        await WithSection("trips.islands", false, async () =>
        {
            var html = await GetAsync("/ekdromes?trip=islands");

            Assert.DoesNotContain("data-trip-tab=\"islands\"", html);
            Assert.DoesNotContain("data-trip-panel=\"islands\"", html);
            Assert.DoesNotContain("Νησιά σε 2–3 ημέρες", html);
            Assert.False(IsHidden(Panel(html, "day")));
            Assert.Contains("aria-current=\"page\"", TabLink(html, "day"));
            Assert.Equal(2, Regex.Matches(html, "data-trip-tab=").Count);
        });

        Assert.Contains("data-trip-tab=\"islands\"", await GetAsync("/ekdromes"));
    }

    [Fact]
    public async Task HidingTheDay_SelectsTheFirstActiveTrip()
    {
        await WithSection("trips.day", false, async () =>
        {
            var html = await GetAsync("/ekdromes");

            Assert.DoesNotContain("data-trip-panel=\"day\"", html);
            Assert.False(IsHidden(Panel(html, "islands")));
            Assert.Contains("aria-current=\"page\"", TabLink(html, "islands"));
        });
    }

    [Fact]
    public async Task HidingEveryTrip_RendersNoTabsAndNoPanels()
    {
        await WithSection("trips.day", false, () => WithSection("trips.islands", false, () => WithSection("trips.week", false, async () =>
        {
            var html = await GetAsync("/ekdromes");

            Assert.DoesNotContain("data-trip-tabs", html);
            Assert.DoesNotContain("data-trip-panel", html);
            Assert.Contains("id=\"booking\"", html);
        })));
    }

    public static TheoryData<string, string[]> SeededText() => new()
    {
        {
            "/ekdromes",
            [
                "Διαλέξτε την εκδρομή σας",
                "Όλες οι εκδρομές είναι ιδιωτικές",
                "Ημερήσια κρουαζιέρα στο Σούνιο",
                "6 ώρες στην Αθηναϊκή Ριβιέρα",
                "Πρωινή κρουαζιέρα με μεσημεριανό",
                "10:00 έως 16:00",
                "Κρουαζιέρα ηλιοβασιλέματος με δείπνο",
                "Περιλαμβάνονται",
                "Εξοπλισμός σνόρκελ",
                "Δεν περιλαμβάνονται",
                "Μεταφορά από την Αθήνα (κατόπιν αιτήματος)",
                "Ιδιωτικό σκάφος, έως [ΑΡ. ΑΤΟΜΩΝ] άτομα",
                "Νησιά σε 2–3 ημέρες",
                "Δυτικές Κυκλάδες",
                "Κέα · Κύθνος · Σέριφος",
                "Ταξιδέψτε ελαφριά",
                "Μαλακή τσάντα αντί για βαλίτσα",
                "Έως [ΑΡ. ΑΤΟΜΩΝ] άτομα",
                "Εβδομαδιαία ναύλωση με σκίπερ",
                "Πακέτο ναύλωσης (σεντόνια, γκάζι, λέμβος, καθαρισμός)",
                "[ΠΟΣΟ]",
                "[Ημέρες προς επιβεβαίωση]",
                "Κλείστε ημερομηνία",
                "Εκδρομή 2–3 ημερών από",
                "Τη διαθεσιμότητα και την ασφαλή πληρωμή",
                "Κράτηση στο [Πλατφόρμα Α]",
                "Κράτηση στο [Πλατφόρμα Β]",
                "Ο ιστότοπος κρατήσεων ανοίγει σε νέα καρτέλα",
                "Ερωτήσεις; Καλέστε στο",
                "30 694 005 7799",
                "Χρήσιμες πληροφορίες",
                "Κακοκαιρία",
                "Γλώσσες στο σκάφος",
                "<p>Ελληνικά και αγγλικά.</p>",
                "Εκδρομές με καταμαράν και κράτηση · Saronida Sailing",
                "Ημερήσια κρουαζιέρα στο Σούνιο, νησιά του Σαρωνικού σε 2–3 ημέρες",
                "<html lang=\"el\">",
            ]
        },
        {
            "/en/trips",
            [
                "Choose your trip",
                "Every trip is private, with a skipper on board.",
                "Day cruise to Sounion",
                "6 hours on the Athens Riviera",
                "Morning cruise with lunch",
                "10:00 to 16:00",
                "Sunset cruise with dinner",
                "Included",
                "Snorkelling gear",
                "Not included",
                "Transfer from Athens (on request)",
                "Private boat, up to [MAX] people",
                "Islands in 2–3 days",
                "Western Cyclades",
                "Kea · Kythnos · Serifos",
                "Travel light",
                "A soft bag instead of a suitcase",
                "Up to [MAX] people",
                "Weekly charter with skipper",
                "Charter package (linen, gas, dinghy, cleaning)",
                "[AMOUNT]",
                "[Days to be confirmed]",
                "Book a date",
                "2–3 day trip from",
                "Our booking partners handle availability and secure payment.",
                "Book on [Platform A]",
                "Book on [Platform B]",
                "The booking site opens in a new tab",
                "Questions? Call",
                "30 694 005 7799",
                "Useful information",
                "Bad weather",
                "Languages on board",
                "<p>Greek and English.</p>",
                "Catamaran trips and booking · Saronida Sailing",
                "A day cruise to Sounion, the Saronic islands in 2–3 days or a weekly charter with skipper.",
                "<html lang=\"en\">",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(SeededText))]
    public async Task Trips_RendersEverySeededSection_InTheVisitorsLanguage(string url, string[] expected)
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
    public async Task Lists_RenderTheSeededItems()
    {
        var html = await GetAsync("/ekdromes");

        Assert.Equal(2, Regex.Matches(html, "<li class=\"trip-slot\"").Count);
        Assert.Equal(2, Regex.Matches(html, "<li class=\"trip-route\"").Count);
        Assert.Equal(3, Regex.Matches(html, "<li class=\"trip-price-line\"").Count);
        Assert.Equal(5, Regex.Matches(html, "<details class=\"accordion\"").Count);
        Assert.Equal(5, Regex.Matches(html, "<summary>").Count);
        Assert.Contains("<li><span>Σκίπερ</span></li>", html);
    }

    [Fact]
    public async Task BookingPlatformLinks_OpenInANewTab_AndSayWhenTheyDo()
    {
        var html = await GetAsync("/ekdromes");

        var platforms = Regex.Matches(html, "<a class=\"btn btn--cta btn--block\"[^>]*>.*?</a>", RegexOptions.Singleline);
        Assert.Equal(2, platforms.Count);
        foreach (Match link in platforms)
        {
            Assert.Contains("href=\"https://example.com/\"", link.Value);
            Assert.Contains("target=\"_blank\"", link.Value);
            Assert.Contains("rel=\"noopener\"", link.Value);
            Assert.Contains("(ανοίγει σε νέα καρτέλα)", link.Value);
        }
    }

    [Fact]
    public async Task Phone_IsAContentDrivenTelLink_InTheBookingBox()
    {
        var html = await GetAsync("/ekdromes");

        var questions = Regex.Match(html, "<div class=\"trip-booking__questions\">.*?</div>", RegexOptions.Singleline).Value;
        Assert.Contains("href=\"tel:&#x2B;306940057799\"", questions);
        Assert.DoesNotContain("target=", questions);
    }

    [Theory]
    [InlineData("/ekdromes", "day", "Ημερήσια κρουαζιέρα από")]
    [InlineData("/ekdromes?trip=islands", "islands", "Εκδρομή 2–3 ημερών από")]
    [InlineData("/en/trips?trip=week", "week", "Weekly charter from")]
    public async Task BookingBoxAndStickyBar_ShowOnlyTheSelectedTripsLabelAndPrice(string url, string selected, string label)
    {
        var html = await GetAsync(url);

        var pairs = Regex.Matches(html, "<[a-z]+ [^>]*data-trip-price=\"(day|islands|week)\"[^>]*>.*?</strong>", RegexOptions.Singleline);
        Assert.Equal(6, pairs.Count);
        foreach (Match pair in pairs)
        {
            var visible = pair.Groups[1].Value == selected;
            Assert.Equal(!visible, IsHidden(Regex.Match(pair.Value, "^<[^>]*>").Value));
            Assert.Contains("<strong>[", pair.Value);
        }

        Assert.All(pairs.Where(p => p.Groups[1].Value == selected), p => Assert.Contains(label, p.Value));
    }

    [Fact]
    public async Task StickyBar_JumpsToTheBookingSection()
    {
        var html = await GetAsync("/ekdromes");

        var sticky = html[html.IndexOf("class=\"sticky-book\"", StringComparison.Ordinal)..];
        Assert.Contains("<a class=\"btn btn--cta\" href=\"#booking\">Κράτηση</a>", sticky);
        Assert.Contains("id=\"booking\"", html);
    }

    [Fact]
    public async Task Prices_ComeFromTheSharedGroup_AndFollowAnEdit()
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        var original = (await content.GetPageContentAsync("trips", "el")).GetField("shared.prices.islands")!.Value;
        Assert.True((await content.ApplyChangesAsync(
            [new SetField("shared.prices.islands", "el", ContentType.Text, "180 EUR")], Editor)).Succeeded);
        try
        {
            var trips = await GetAsync("/ekdromes?trip=islands");
            Assert.Contains("<strong>180 EUR</strong>", trips);
            Assert.Contains("<strong>[ΤΙΜΗ]</strong>", trips);

            var home = await GetAsync("/");
            var card = Regex.Match(home, "<article class=\"trip-card\"[^>]*>(?:(?!</article>).)*Γύρος στα νησιά.*?</article>", RegexOptions.Singleline).Value;
            Assert.Contains("<strong>180 EUR</strong>", card);
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync(
                [new SetField("shared.prices.islands", "el", ContentType.Text, original)], Editor)).Succeeded);
        }
    }
}
