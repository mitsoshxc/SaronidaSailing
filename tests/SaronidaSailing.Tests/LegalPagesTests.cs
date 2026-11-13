using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class LegalPagesTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static readonly ContentEditor Editor = new("legal-test@example.com", null);

    private Task<string> GetAsync(string url) => NewClient(factory).GetStringAsync(url);

    private static IReadOnlyList<string> SectionIds(string html) =>
        Regex.Matches(html, "<section[^>]*class=\"legal-section\"[^>]*>")
            .Select(m => Regex.Match(m.Value, "id=\"(section-[0-9a-f-]+)\"").Groups[1].Value)
            .Where(id => id.Length > 0)
            .ToList();

    private static IReadOnlyList<(string Href, string Title)> TocLinks(string html) =>
        Regex.Matches(html, "<a class=\"legal-toc__link\" href=\"([^\"]*)\">(.*?)</a>")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
            .ToList();

    public static TheoryData<string, string, int, string> Pages() => new()
    {
        { "/oroi", "el", 7, "Όροι κράτησης" },
        { "/en/terms", "en", 7, "Booking terms" },
        { "/aporrito", "el", 7, "Πολιτική απορρήτου" },
        { "/en/privacy", "en", 7, "Privacy policy" },
        { "/prosvasimotita", "el", 6, "Δήλωση προσβασιμότητας" },
        { "/en/accessibility", "en", 6, "Accessibility statement" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_RendersFromTheSeed_WithATocMatchingTheBody(string url, string lang, int sections, string title)
    {
        var html = await GetAsync(url);

        Assert.Contains($"<html lang=\"{lang}\">", html);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Contains($">{title}</h1>", html);
        Assert.DoesNotContain("data-cms", html);
        Assert.Contains(lang == "el" ? "Τελευταία ενημέρωση: [ΗΜΕΡΟΜΗΝΙΑ]" : "Last updated: [DATE]", html);

        var ids = SectionIds(html);
        var toc = TocLinks(html);
        Assert.Equal(sections, ids.Count);
        Assert.Equal(ids.Select(id => $"#{id}"), toc.Select(t => t.Href));
        var headings = Regex.Matches(html, "<section[^>]*class=\"legal-section\"[^>]*>\\s*<h2>(.*?)</h2>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value);
        Assert.Equal(headings, toc.Select(t => t.Title));
        Assert.All(toc, t => Assert.DoesNotMatch(@"^\d", t.Title));

        var navLabel = lang == "el" ? "Σε αυτή τη σελίδα" : "On this page";
        Assert.Contains($"<nav class=\"legal-toc\" aria-label=\"{navLabel}\">", html);
        Assert.Equal(sections, Regex.Matches(html, "<section[^>]*class=\"legal-section\"").Count);
        var summary = html[html.IndexOf("legal-summary__list", StringComparison.Ordinal)..];
        Assert.Equal(4, Regex.Matches(summary[..summary.IndexOf("</ul>", StringComparison.Ordinal)], "<li><span>").Count);
        Assert.Contains("<ul class=\"check-list legal-summary__list\">", html);
    }

    [Theory]
    [InlineData("/oroi", "/aporrito", "Πολιτική απορρήτου", "Δείτε επίσης")]
    [InlineData("/en/terms", "/en/privacy", "Privacy policy", "See also")]
    [InlineData("/aporrito", "/oroi", "Όροι", "Δείτε επίσης")]
    [InlineData("/en/privacy", "/en/terms", "Terms", "See also")]
    public async Task SeeAlso_LinksToTheOtherLegalPage(string url, string target, string name, string label)
    {
        var html = await GetAsync(url);

        var card = Regex.Match(html, "<a class=\"legal-also\" href=\"([^\"]*)\">.*?</a>", RegexOptions.Singleline);
        Assert.True(card.Success);
        Assert.Equal(target, card.Groups[1].Value);
        Assert.Contains($"<strong>{name}</strong>", card.Value);
        Assert.Contains(label, card.Value);
    }

    [Theory]
    [InlineData("/prosvasimotita")]
    [InlineData("/en/accessibility")]
    public async Task Accessibility_HasNoSeeAlsoCard(string url)
    {
        Assert.DoesNotContain("legal-also", await GetAsync(url));
    }

    [Theory]
    [InlineData("/oroi", "/epikoinonia", "Κάτι δεν είναι σαφές;")]
    [InlineData("/en/terms", "/en/contact", "Something unclear?")]
    [InlineData("/aporrito", "/epikoinonia", "Ερωτήσεις για τα δεδομένα σας;")]
    [InlineData("/en/privacy", "/en/contact", "Questions about your data?")]
    [InlineData("/prosvasimotita", "/epikoinonia", "Χρειάζεστε βοήθεια;")]
    [InlineData("/en/accessibility", "/en/contact", "Need help?")]
    public async Task ContactCard_IsOneLinkToContact(string url, string contactPath, string lead)
    {
        var html = await GetAsync(url);

        var card = Regex.Match(html, "<a class=\"legal-contact\" href=\"([^\"]*)\">.*?</a>", RegexOptions.Singleline);
        Assert.True(card.Success);
        Assert.Equal(contactPath, card.Groups[1].Value);
        Assert.Contains(lead, card.Value);
    }

    [Fact]
    public async Task Terms_CancellationsKeepTheirLeads_AsStrongParagraphs()
    {
        var html = await GetAsync("/oroi");

        Assert.Contains("<p><strong>Ημερήσιες κρουαζιέρες:</strong>", html);
        Assert.Contains("<p><strong>Ναυλώσεις πολλών ημερών:</strong>", html);
        Assert.Contains("[Προς επιβεβαίωση]", html);
        Assert.Contains("[AMOUNT]", html);
    }

    [Theory]
    [InlineData("/aporrito", "90 ημέρες")]
    [InlineData("/en/privacy", "for 90 days")]
    public async Task Privacy_DescribesWhatWasBuilt(string url, string retention)
    {
        var html = await GetAsync(url);

        Assert.Contains(retention, html);
        Assert.Contains("Cloudflare Web Analytics", html);
        Assert.Contains("<a href=\"https://www.dpa.gr/\">dpa.gr</a>", html);
        Assert.DoesNotContain("Στατιστικά", html);
        Assert.DoesNotContain("[διάστημα]", html);
    }

    [Fact]
    public async Task Accessibility_PhoneIsATelLink_AndEmailStaysPlainText()
    {
        var html = await GetAsync("/prosvasimotita");

        Assert.Matches(@"<a href=""tel:(\+|&#x2B;)306940057799"">(\+|&#x2B;)30 694 005 7799</a>", html);
        Assert.DoesNotContain("mailto:[EMAIL]", html);
        Assert.Contains("[EMAIL]", html);
        Assert.Contains("[Πλήρης ή μερική συμμόρφωση", html);
    }

    private async Task<Guid> SectionIdAsync(string pageKey, int index)
    {
        using var scope = factory.Services.CreateScope();
        var content = await scope.ServiceProvider.GetRequiredService<IContentService>().GetPageContentAsync(pageKey, "el");
        return content.GetList($"{pageKey}.sections").OrderBy(e => e.SortOrder).ElementAt(index).Id;
    }

    private async Task SetActiveAsync(string pageKey, Guid id, bool active)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IContentService>()
            .ApplyChangesAsync([new SetListItemActive($"{pageKey}.sections", id, active)], Editor);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("/oroi", "terms")]
    [InlineData("/en/privacy", "privacy")]
    public async Task HiddenSection_DisappearsFromBodyAndToc_ForVisitors(string url, string pageKey)
    {
        var before = SectionIds(await GetAsync(url));
        var hidden = await SectionIdAsync(pageKey, 1);
        await SetActiveAsync(pageKey, hidden, false);
        try
        {
            var html = await GetAsync(url);

            var ids = SectionIds(html);
            Assert.Equal(before.Count - 1, ids.Count);
            Assert.DoesNotContain($"section-{hidden}", html);
            Assert.Equal(ids.Select(id => $"#{id}"), TocLinks(html).Select(t => t.Href));
        }
        finally
        {
            await SetActiveAsync(pageKey, hidden, true);
        }
    }

    [Fact]
    public async Task HiddenSection_StaysInBodyAndToc_ForTheEditor()
    {
        var hidden = await SectionIdAsync("terms", 1);
        await SetActiveAsync("terms", hidden, false);
        try
        {
            var client = NewClient(factory);
            await LoginWithTotpAsync(client, await AddTwoFactorAdminAsync(factory, "legal-editor@example.com"));

            var html = await client.GetStringAsync("/oroi");

            var ids = SectionIds(html);
            Assert.Equal(7, ids.Count);
            Assert.Contains($"section-{hidden}", ids);
            Assert.Equal(ids.Select(id => $"#{id}"), TocLinks(html).Select(t => t.Href));
            Assert.Contains("data-cms-active=\"false\"", html);
        }
        finally
        {
            await SetActiveAsync("terms", hidden, true);
        }
    }

    [Fact]
    public async Task HiddenBodySection_EmptiesTheTocForVisitors()
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        Assert.True((await content.ApplyChangesAsync([new SetSectionActive("accessibility.body", false)], Editor)).Succeeded);
        try
        {
            var html = await GetAsync("/prosvasimotita");

            Assert.Empty(SectionIds(html));
            Assert.Empty(TocLinks(html));
            Assert.DoesNotContain("legal-toc", html);
        }
        finally
        {
            Assert.True((await content.ApplyChangesAsync([new SetSectionActive("accessibility.body", true)], Editor)).Succeeded);
        }
    }

    [Fact]
    public void LegalSeeds_LoadAndValidate()
    {
        var groups = new EmbeddedContentSeedSource().GetGroups();

        foreach (var name in new[] { "terms", "privacy", "accessibility" })
        {
            var group = groups.Single(g => g.Name == name);
            Assert.Equal(1, group.Version);
            Assert.Equal([$"{name}.sections", $"{name}.summary-items"], group.Lists.Select(l => l.ListKey).Order());
            Assert.Equal(4, group.Lists.Single(l => l.ListKey == $"{name}.summary-items").Items.Count);
            Assert.All(
                group.Lists.Single(l => l.ListKey == $"{name}.sections").Items,
                item => Assert.Equal(ContentType.RichText, item.Fields.Single(f => f.Key == "body").Type));
            Assert.Contains(group.Fields, f => f.Key == $"{name}.seo.title");
            Assert.Contains(group.Fields, f => f.Key == $"{name}.seo.description");
        }
    }
}
