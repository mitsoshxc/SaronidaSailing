using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Models;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class BoatPageTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static readonly ContentEditor Editor = new("boat-test@example.com", null);

    private Task<string> GetAsync(string url) => NewClient(factory).GetStringAsync(url);

    private static string Slice(string html, string startMarker, string endMarker)
    {
        var start = html.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{startMarker}' not found.");
        var end = html.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"'{endMarker}' not found after '{startMarker}'.");
        return html[start..end];
    }

    private static string Booking(string html) => Slice(html, "<section class=\"trip-booking boat-booking\"", "</section>");

    private static string Gallery(string html) => Slice(html, "class=\"boat-gallery-section\"", "<div class=\"wrap boat-layout\">");

    private static string Grid(string html) => Slice(html, "<ul class=\"boat-gallery__grid\"", "</ul>");

    public static TheoryData<string, string[]> SeededText() => new()
    {
        {
            "/skafos",
            [
                "<html lang=\"el\">",
                "Το σκάφος: καταμαράν Lagoon 500 · Saronida Sailing",
                "Γνωρίστε το Lagoon 500: τεχνικά χαρακτηριστικά",
                "Το σκάφος μας",
                ">Lagoon 500</h1>",
                "Ιστιοπλοϊκό καταμαράν · 2008 · λιμάνι Άγιος Νικόλαος",
                "Δύο γάστρες, flybridge και περισσότερος χώρος",
                "Τεχνικά χαρακτηριστικά",
                "15,54 μ.",
                "Επιβάτες, με διανυκτέρευση",
                "[ΑΡ. ΑΤΟΜΩΝ]",
                "2 × [75] HP",
                "Εργοστασιακά στοιχεία του Lagoon 500.",
                "Η ζωή στο σκάφος",
                "Τραμπολίνα στην πλώρη",
                "[X] διπλές καμπίνες",
                "Περιλαμβάνονται",
                "Σωσίβια για όλους",
                "Στοιχεία ναύλωσης",
                "Επιβίβαση",
                "Αποβίβαση",
                "17:00",
                "Πακέτο ναύλωσης (σεντόνια, γκάζι, λέμβος, τελικός καθαρισμός)",
                "Εγγύηση (επιστρέφεται)",
                "Θέλετε άλλη ώρα ή άλλο σημείο επιβίβασης;",
                "Έτοιμοι να επιβιβαστείτε;",
                "Εβδομαδιαία ναύλωση με σκίπερ",
                "[ΤΙΜΗ] / εβδ.",
                "Σκίπερ στο σκάφος",
                "Έως [ΑΡ. ΑΤΟΜΩΝ] επιβάτες",
                "Δείτε διαθεσιμότητα",
                "Ή ημερήσια κρουαζιέρα από",
                "Ερωτήσεις; Καλέστε στο",
                "30 694 005 7799",
                "Ημερομηνίες",
            ]
        },
        {
            "/en/boat",
            [
                "<html lang=\"en\">",
                "The boat: Lagoon 500 catamaran · Saronida Sailing",
                "Meet the Lagoon 500: specifications",
                "Our boat",
                ">Lagoon 500</h1>",
                "Sailing catamaran · 2008 · Agios Nikolaos harbour",
                "Two hulls, a flybridge and more deck space than many villas.",
                "Specifications",
                "15.54 m",
                "Passengers, overnight",
                "[MAX]",
                "2 × [75] HP",
                "Factory data for the Lagoon 500.",
                "Life on board",
                "Trampoline at the bow",
                "[X] double cabins",
                "Included",
                "Life jackets for everyone",
                "Charter details",
                "Boarding",
                "Disembarking",
                "17:00",
                "Charter package (linen, gas, dinghy, final cleaning)",
                "Security deposit (refundable)",
                "Want a different time or boarding point?",
                "Ready to come aboard?",
                "Weekly charter with skipper",
                "[PRICE] / week",
                "Skipper on board",
                "Up to [MAX] passengers",
                "Check availability",
                "Or a day cruise from",
                "Questions? Call",
                "30 694 005 7799",
                "Dates",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(SeededText))]
    public async Task Boat_RendersEverySeededSection_InTheVisitorsLanguage(string url, string[] expected)
    {
        var html = await GetAsync(url);

        foreach (var text in expected)
        {
            Assert.Contains(text, html);
        }

        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.DoesNotContain("data-cms", html);
        Assert.Single(Regex.Matches(html, "<main[ >]"));
    }

    [Fact]
    public async Task Boat_Greek_HasNoEnglishText()
    {
        var html = await GetAsync("/skafos");

        Assert.DoesNotContain("Life on board", html);
        Assert.DoesNotContain("Check availability", html);
    }

    [Fact]
    public async Task Specs_AreADefinitionList_OfTenTiles()
    {
        var html = await GetAsync("/skafos");

        var specs = Slice(html, "<dl class=\"boat-specs__grid\">", "</dl>");
        Assert.Equal(10, Regex.Matches(specs, "<div class=\"boat-spec\"><dt>[^<]+</dt><dd>[^<]+</dd></div>").Count);
        Assert.Contains("<div class=\"boat-spec\"><dt>Μήκος</dt><dd>15,54 μ.</dd></div>", specs);
    }

    [Fact]
    public async Task Lists_RenderTheSeededItems()
    {
        var html = await GetAsync("/skafos");

        Assert.Equal(5, Regex.Matches(html, "<li class=\"boat-life__item\"><strong>").Count);
        Assert.Equal(8, Regex.Matches(Slice(html, "boat-included__list", "</ul>"), "<li><span>").Count);
        Assert.Equal(2, Regex.Matches(html, "<div class=\"boat-time\"><span class=\"boat-time__label\">").Count);
        Assert.Equal(3, Regex.Matches(Booking(html), "<li><span>").Count);
        Assert.Equal(3, Regex.Matches(html, "<li class=\"boat-cost\">").Count);
    }

    [Fact]
    public async Task CharterCosts_AreOneSharedList_OnBoatAndTrips()
    {
        var boat = await GetAsync("/skafos");
        var trips = await GetAsync("/ekdromes");

        foreach (var label in new[] { "Εγγύηση (επιστρέφεται)", "Καύσιμα", "τελικός καθαρισμός" })
        {
            Assert.Contains(label, boat);
            Assert.Contains(label, trips);
        }

        Assert.Contains("<strong class=\"boat-cost__value\">[ΠΟΣΟ]</strong>", boat);
    }

    [Fact]
    public async Task EditingASharedCharterCost_ChangesBothPages()
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        var cost = (await content.GetPageContentAsync("boat", "el")).GetList("shared.charter-costs").Single(e => e.Fields["label"].Value == "Καύσιμα");
        var original = cost.Fields["value"].Value;
        Assert.True((await content.ApplyChangesAsync(
            [new SetField("shared.charter-costs.value", "el", ContentType.Text, "77 EUR", cost.Id)], Editor)).Succeeded);
        try
        {
            Assert.Contains("<strong class=\"boat-cost__value\">77 EUR</strong>", await GetAsync("/skafos"));
            Assert.Contains("<strong class=\"trip-price-line__value\">77 EUR</strong>", await GetAsync("/ekdromes"));
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync(
                [new SetField("shared.charter-costs.value", "el", ContentType.Text, original, cost.Id)], Editor)).Succeeded);
        }
    }

    [Theory]
    [InlineData("/skafos", "/ekdromes?trip=week", "/ekdromes?trip=day")]
    [InlineData("/en/boat", "/en/trips?trip=week", "/en/trips?trip=day")]
    public async Task BookingBox_LinksToTheWeekAndDayTrips(string url, string week, string day)
    {
        var booking = Booking(await GetAsync(url));

        Assert.Contains($"<a class=\"btn btn--cta btn--block boat-booking__cta\" href=\"{week}\">", booking);
        Assert.Contains($"<a class=\"boat-booking__alt\" href=\"{day}\">", booking);
        Assert.Contains("id=\"booking\"", booking);
        Assert.Contains("aria-labelledby=\"boat-booking-title\"", booking);
        Assert.Contains("<h2 class=\"boat-booking__title\" id=\"boat-booking-title\">", booking);
    }

    [Theory]
    [InlineData("/skafos", "[ΤΙΜΗ]")]
    [InlineData("/en/boat", "[PRICE]")]
    public async Task BookingBox_ShowsTheSharedPrices_AndTheKeepContentPhone(string url, string price)
    {
        var booking = Booking(await GetAsync(url));

        Assert.Contains($"<strong>{price} /", booking);
        Assert.Contains($"<strong>{price}</strong></a>", booking);
        var questions = Regex.Match(booking, "<div class=\"trip-booking__questions\">.*?</div>", RegexOptions.Singleline).Value;
        Assert.Contains("href=\"tel:&#x2B;306940057799\"", questions);
        Assert.DoesNotContain("target=", questions);
    }

    [Fact]
    public async Task StickyBar_ShowsTheWeekPrice_AndJumpsToTheBookingBox()
    {
        var html = await GetAsync("/skafos");

        var sticky = html[html.IndexOf("class=\"sticky-book\"", StringComparison.Ordinal)..];
        Assert.Contains("<span class=\"sticky-book__label\">Εβδομαδιαία ναύλωση με σκίπερ</span><strong>[ΤΙΜΗ] / εβδ.</strong>", sticky);
        Assert.Contains("<a class=\"btn btn--cta\" href=\"#booking\">Ημερομηνίες</a>", sticky);
    }

    [Fact]
    public async Task Gallery_WithNoImages_ShowsTheTilesButNoAllPhotosButtonOrDialog()
    {
        var html = await GetAsync("/skafos");

        var gallery = Gallery(html);
        Assert.Equal(5, Regex.Matches(Grid(html), "<li class=\"boat-gallery__tile").Count);
        Assert.DoesNotContain("<img", gallery);
        Assert.DoesNotContain("data-gallery-all", gallery);
        Assert.DoesNotContain("<dialog", html);
    }

    [Fact]
    public async Task Gallery_WithImages_CountsOnlyTilesThatHaveOne_AndRendersTheLightboxOnce()
    {
        var (cover, photo, photoItem) = await SetImagesAsync();
        try
        {
            foreach (var (url, all, photos, close, previous, next) in new[]
            {
                ("/skafos", "Όλες οι φωτογραφίες (2)", "Φωτογραφίες", "Κλείσιμο", "Προηγούμενη φωτογραφία", "Επόμενη φωτογραφία"),
                ("/en/boat", "All photos (2)", "Photos", "Close", "Previous photo", "Next photo"),
            })
            {
                var html = await GetAsync(url);

                var grid = Grid(html);
                var images = Regex.Matches(grid, "<img [^>]*>").Select(m => m.Value).ToList();
                Assert.Equal(2, images.Count);
                Assert.Single(images, i => i.Contains(cover.Slug + "-"));
                Assert.Single(images, i => i.Contains(photo.Slug + "-"));
                Assert.Contains("loading=\"eager\"", images.Single(i => i.Contains(cover.Slug + "-")));
                Assert.Contains("sizes=\"(min-width: 960px) 50vw, 100vw\"", images.Single(i => i.Contains(cover.Slug + "-")));
                Assert.Contains("srcset=\"", images[0]);
                Assert.Single(Regex.Matches(html, Regex.Escape(cover.Slug + "-") + "[0-9a-f]{16}-960\\.webp\" srcset"));
                Assert.Equal(5, Regex.Matches(grid, "<li class=\"boat-gallery__tile").Count);

                Assert.Contains($"<button type=\"button\" class=\"boat-gallery__all\" data-gallery-all hidden>{all}</button>", html);
                Assert.Single(Regex.Matches(html, "<dialog "));
                var dialog = Slice(html, "<dialog ", "</dialog>");
                Assert.Contains($"<dialog class=\"lightbox\" aria-label=\"{photos}\" data-lightbox>", dialog);
                Assert.Contains("aria-live=\"polite\" data-lightbox-counter", dialog);
                Assert.Contains($"<span class=\"visually-hidden\">{close}</span>", dialog);
                Assert.Contains($"<span class=\"visually-hidden\">{previous}</span>", dialog);
                Assert.Contains($"<span class=\"visually-hidden\">{next}</span>", dialog);
                Assert.Contains("<img class=\"lightbox__img\" alt=\"\" data-lightbox-img />", dialog);
                Assert.DoesNotContain("src=", dialog.Replace("<img class=\"lightbox__img\" alt=\"\" data-lightbox-img />", string.Empty));
                Assert.True(
                    html.IndexOf("<dialog ", StringComparison.Ordinal) > html.IndexOf("</ul>", html.IndexOf("boat-gallery__grid", StringComparison.Ordinal), StringComparison.Ordinal));
            }
        }
        finally
        {
            await ClearImagesAsync(photoItem);
        }
    }

    [Fact]
    public async Task PhotoCount_IgnoresInactiveItems_AndMissingImages()
    {
        var (_, _, photoItem) = await SetImagesAsync();
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        try
        {
            Assert.Equal(2, BoatGallery.CountPhotos(await content.GetPageContentAsync("boat", "el")));
            Assert.True((await content.ApplyChangesAsync([new SetListItemActive("boat.gallery-photos", photoItem, false)], Editor)).Succeeded);
            Assert.Equal(1, BoatGallery.CountPhotos(await content.GetPageContentAsync("boat", "en")));
            Assert.Contains("(1)", await GetAsync("/skafos"));
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync([new SetListItemActive("boat.gallery-photos", photoItem, true)], Editor)).Succeeded);
            await ClearImagesAsync(photoItem);
        }
    }

    [Fact]
    public async Task HidingTheBookingBox_RemovesIt_ButKeepsTheRest()
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        Assert.True((await content.ApplyChangesAsync([new SetSectionActive("boat.booking", false)], Editor)).Succeeded);
        try
        {
            var html = await GetAsync("/skafos");

            Assert.DoesNotContain("id=\"booking\"", html);
            Assert.DoesNotContain("Έτοιμοι να επιβιβαστείτε;", html);
            Assert.Contains("Τεχνικά χαρακτηριστικά", html);
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync([new SetSectionActive("boat.booking", true)], Editor)).Succeeded);
        }
    }

    private async Task<(MediaAsset Cover, MediaAsset Photo, Guid PhotoItem)> SetImagesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaService>();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        var cover = (await media.ImportAsync(new MemoryStream(MediaTestImages.Photo(1200, 800, seed: 301)), "boat-cover-test.jpg")).Asset!;
        var photo = (await media.ImportAsync(new MemoryStream(MediaTestImages.Photo(1200, 800, seed: 302)), "boat-photo-test.jpg")).Asset!;
        var first = (await content.GetPageContentAsync("boat", "el")).GetList("boat.gallery-photos")[0].Id;
        var result = await content.ApplyChangesAsync(
            [
                new SetField("boat.gallery.cover", "el", ContentType.Image, cover.Id.ToString()),
                new SetField("boat.gallery-photos.image", "el", ContentType.Image, photo.Id.ToString(), first),
            ],
            Editor);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return (cover, photo, first);
    }

    private async Task ClearImagesAsync(Guid photoItem)
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        var result = await content.ApplyChangesAsync(
            [
                new SetField("boat.gallery.cover", "el", ContentType.Image, string.Empty),
                new SetField("boat.gallery-photos.image", "el", ContentType.Image, string.Empty, photoItem),
            ],
            Editor);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }
}
