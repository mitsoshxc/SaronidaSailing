using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Tests;

public class SitePageKeysTests
{
    [Fact]
    public void Keys_MatchTheWebSitePages()
    {
        Assert.Equal(SitePageKeys.All.Order(), SitePages.All.Select(p => p.Key).Order());
    }

    [Theory]
    [InlineData("page:home", true)]
    [InlineData("page:trips", true)]
    [InlineData("page:accessibility", true)]
    [InlineData("page:nope", false)]
    [InlineData("page:", false)]
    [InlineData("page:Trips", false)]
    [InlineData("PAGE:trips", false)]
    [InlineData("page: trips", false)]
    [InlineData("pages:trips", false)]
    public void TryParse_PageUrl_AcceptsOnlyKnownKeys(string url, bool valid)
    {
        var json = $$"""{"url":"{{url}}","label":"Go"}""";

        Assert.Equal(valid, ContentLink.TryParse(json, out var link));
        if (valid)
        {
            Assert.Equal(url, link!.Url);
            Assert.Equal(json.Replace(" ", ""), link.ToJson().Replace(" ", ""));
        }
    }

    [Fact]
    public void PageUrl_IsNotAnAllowedRichTextUrl()
    {
        Assert.False(ContentLink.IsAllowedUrl("page:trips"));
    }

    [Theory]
    [InlineData("""{"url":"page:nope","label":"Go"}""", "nope")]
    [InlineData("""{"url":"page:trips","label":"Go"}""", null)]
    [InlineData("""{"url":"https://a.b","label":"Go"}""", null)]
    [InlineData("not json", null)]
    public void FindUnknownPageKey_ReportsOnlyUnknownPages(string json, string? expected)
    {
        Assert.Equal(expected, ContentLink.FindUnknownPageKey(json));
    }
}

[Collection(WebHostCollection.Name)]
public class SeededMenuTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static string Between(string html, string start, string end)
    {
        var from = html.IndexOf(start, StringComparison.Ordinal);
        var to = html.IndexOf(end, from, StringComparison.Ordinal);
        return html[from..to];
    }

    [Theory]
    [InlineData("/", "el", "")]
    [InlineData("/ekdromes", "el", "trips")]
    [InlineData("/skafos", "el", "boat")]
    [InlineData("/epikoinonia", "el", "contact")]
    [InlineData("/oroi", "el", "terms")]
    [InlineData("/en/", "en", "")]
    [InlineData("/en/trips", "en", "trips")]
    [InlineData("/en/boat", "en", "boat")]
    [InlineData("/en/contact", "en", "contact")]
    [InlineData("/en/privacy", "en", "privacy")]
    public async Task HeaderMenu_RendersSeededItemsInOrder(string path, string locale, string current)
    {
        var html = await AdminTestHelpers.NewClient(factory).GetStringAsync(path);
        var nav = Between(html, "<nav class=\"site-nav\"", "</nav>");
        var links = Regex.Matches(nav, "<a [^>]*>.*?</a>").Select(m => m.Value).ToList();
        var en = locale == "en";
        string Aria(string key) => key == current ? " aria-current=\"page\"" : "";
        string[] expected =
        [
            $"<a class=\"site-nav__link\" href=\"{(en ? "/en/trips" : "/ekdromes")}\"{Aria("trips")}>{(en ? "Trips" : "Εκδρομές")}</a>",
            $"<a class=\"site-nav__link\" href=\"{(en ? "/en/boat" : "/skafos")}\"{Aria("boat")}>{(en ? "The boat" : "Το σκάφος")}</a>",
            "<a class=\"site-nav__link\" href=\"https://www.instagram.com/saronidasailing/\" target=\"_blank\" rel=\"noopener\">Instagram"
                + $"<span class=\"visually-hidden\">{(en ? " (opens in a new tab)" : " (ανοίγει σε νέα καρτέλα)")}</span></a>",
            $"<a class=\"site-nav__link\" href=\"{(en ? "/en/contact" : "/epikoinonia")}\"{Aria("contact")}>{(en ? "Contact" : "Επικοινωνία")}</a>",
        ];

        Assert.Equal(expected, links);
        Assert.DoesNotContain("data-cms-", html);
    }

    [Theory]
    [InlineData("/epikoinonia", "el", "contact")]
    [InlineData("/en/", "en", "")]
    [InlineData("/en/terms", "en", "terms")]
    [InlineData("/aporrito", "el", "privacy")]
    public async Task FooterMenu_RendersSeededPages_AndCookieButtonStaysLast(string path, string locale, string current)
    {
        var html = await AdminTestHelpers.NewClient(factory).GetStringAsync(path);
        var list = Between(html, "<ul class=\"site-footer__pages\">", "</ul>");
        var items = Regex.Matches(list, "<li>(.*?)</li>").Select(m => m.Groups[1].Value).ToList();
        var en = locale == "en";
        (string Key, string Href, string Label)[] pages =
        [
            ("trips", en ? "/en/trips" : "/ekdromes", en ? "Trips &amp; booking" : "Εκδρομές &amp; κράτηση"),
            ("boat", en ? "/en/boat" : "/skafos", en ? "The boat" : "Το σκάφος"),
            ("contact", en ? "/en/contact" : "/epikoinonia", en ? "Contact" : "Επικοινωνία"),
            ("terms", en ? "/en/terms" : "/oroi", en ? "Terms" : "Όροι"),
            ("privacy", en ? "/en/privacy" : "/aporrito", en ? "Privacy policy" : "Πολιτική απορρήτου"),
            ("accessibility", en ? "/en/accessibility" : "/prosvasimotita", en ? "Accessibility" : "Προσβασιμότητα"),
        ];

        Assert.Equal(pages.Length + 1, items.Count);
        for (var i = 0; i < pages.Length; i++)
        {
            var aria = pages[i].Key == current ? " aria-current=\"page\"" : "";
            Assert.Equal($"<a href=\"{pages[i].Href}\"{aria}>{pages[i].Label}</a>", items[i]);
        }

        Assert.StartsWith("<button type=\"button\" class=\"site-footer__cookies\"", items[^1]);
    }

    [Fact]
    public async Task BookButton_IsVisibleByDefault_OnEveryPage()
    {
        var client = AdminTestHelpers.NewClient(factory);

        foreach (var path in new[] { "/", "/ekdromes", "/en/", "/en/contact" })
        {
            Assert.Contains("site-menu__book", await client.GetStringAsync(path));
        }
    }
}

[Collection(WebHostCollection.Name)]
public class MenuEditingTests(SeededWebApplicationFactory factory) : IClassFixture<SeededWebApplicationFactory>
{
    private static readonly string[] Paths = ["/", "/ekdromes", "/skafos", "/en/", "/en/contact", "/en/terms"];

    private async Task<ContentSaveResult> Apply(params ContentChange[] changes)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IContentService>()
            .ApplyChangesAsync(changes, new ContentEditor("menu-test@example.com", null));
    }

    private async Task<Guid> ListItemIdAsync(string listKey, int index)
    {
        using var scope = factory.Services.CreateScope();
        var content = await scope.ServiceProvider.GetRequiredService<IContentService>().GetPageContentAsync("shared", "el");
        return content.GetList(listKey).OrderBy(e => e.SortOrder).ElementAt(index).Id;
    }

    private async Task<HttpClient> AdminClientAsync(string email)
    {
        var client = AdminTestHelpers.NewClient(factory);
        await AdminTestHelpers.LoginWithTotpAsync(client, await AdminTestHelpers.AddTwoFactorAdminAsync(factory, email));
        return client;
    }

    [Fact]
    public async Task HiddenMenuItem_DisappearsForVisitors_RemainsForEditor_OnEveryPage()
    {
        var visitor = AdminTestHelpers.NewClient(factory);
        foreach (var path in Paths)
        {
            await visitor.GetStringAsync(path);
        }

        var instagram = await ListItemIdAsync("shared.menu-header", 2);
        Assert.True((await Apply(new SetListItemActive("shared.menu-header", instagram, false))).Succeeded);

        foreach (var path in Paths)
        {
            var html = await visitor.GetStringAsync(path);
            Assert.DoesNotContain("site-nav__link\" href=\"https://www.instagram.com", html);
            Assert.Equal(3, Regex.Matches(html, "class=\"site-nav__link\"").Count);
        }

        var admin = await AdminClientAsync("menu-editor@example.com");
        var editorHtml = await admin.GetStringAsync("/");

        Assert.Contains("data-cms-list=\"shared.menu-header\"", editorHtml);
        Assert.Contains("data-cms-active=\"false\"", editorHtml);
        Assert.Contains("data-cms-list-template=\"shared.menu-header\" data-cms-add-label=\"Add menu item\"", editorHtml);
        Assert.Contains("data-cms-list-template=\"shared.menu-footer\" data-cms-add-label=\"Add footer link\"", editorHtml);

        Assert.True((await Apply(new SetListItemActive("shared.menu-header", instagram, true))).Succeeded);
    }

    [Fact]
    public async Task AllHeaderItemsHidden_VisitorsGetNoNav_EditorStillGetsList()
    {
        var visitor = AdminTestHelpers.NewClient(factory);
        await visitor.GetStringAsync("/");
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add(await ListItemIdAsync("shared.menu-header", i));
        }

        Assert.True((await Apply([.. ids.Select(id => new SetListItemActive("shared.menu-header", id, false))])).Succeeded);

        var html = await visitor.GetStringAsync("/");
        Assert.DoesNotContain("site-nav", html);

        var editorHtml = await (await AdminClientAsync("menu-editor2@example.com")).GetStringAsync("/");
        Assert.Contains("<ul class=\"site-nav__list\"", editorHtml);

        Assert.True((await Apply([.. ids.Select(id => new SetListItemActive("shared.menu-header", id, true))])).Succeeded);
    }

    [Fact]
    public async Task HiddenBookButton_IsAbsentForVisitors_PresentForEditor()
    {
        var visitor = AdminTestHelpers.NewClient(factory);
        foreach (var path in Paths)
        {
            Assert.Contains("site-menu__book", await visitor.GetStringAsync(path));
        }

        Assert.True((await Apply(new SetSectionActive("shared.book", false))).Succeeded);

        foreach (var path in Paths)
        {
            var html = await visitor.GetStringAsync(path);
            Assert.DoesNotContain("site-menu__book", html);
            Assert.Contains("site-menu__tools", html);
        }

        var editorHtml = await (await AdminClientAsync("menu-editor3@example.com")).GetStringAsync("/en/");
        Assert.Matches("data-cms-section=\"shared.book\"[^>]*data-cms-active=\"false\"", editorHtml);
        Assert.Contains("site-menu__book", editorHtml);

        Assert.True((await Apply(new SetSectionActive("shared.book", true))).Succeeded);
        Assert.Contains("site-menu__book", await visitor.GetStringAsync("/"));
    }

    [Fact]
    public async Task NewPageItem_ShowsOnEveryPage_AndUnknownPageIsRejected()
    {
        var visitor = AdminTestHelpers.NewClient(factory);
        await visitor.GetStringAsync("/en/");
        var id = Guid.CreateVersion7();

        var bad = await Apply(
            new AddListItem("shared.menu-footer", id),
            new SetField("shared.menu-footer.link", "el", ContentType.Link, """{"url":"page:nowhere","label":"X"}""", id));
        Assert.False(bad.Succeeded);

        var good = await Apply(
            new AddListItem("shared.menu-footer", id),
            new SetField("shared.menu-footer.link", "el", ContentType.Link, """{"url":"page:home","label":"Αρχική"}""", id),
            new SetField("shared.menu-footer.link", "en", ContentType.Link, """{"url":"page:home","label":"Home"}""", id));
        Assert.True(good.Succeeded, string.Join("; ", good.Errors));

        Assert.Contains("<a href=\"/en/\">Home</a>", await visitor.GetStringAsync("/en/trips"));
        Assert.Contains("<a href=\"/\" aria-current=\"page\">Αρχική</a>", await visitor.GetStringAsync("/"));
        Assert.True((await Apply(new DeleteListItem("shared.menu-footer", id))).Succeeded);
    }
}
