using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Tests;

public sealed class EmbeddedContentSeedSourceTests : IAsyncLifetime
{
    private string _connectionString = "";

    public async Task InitializeAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    public Task DisposeAsync() => PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));

    [Fact]
    public void ShippedGroups_Load_SortedByName()
    {
        var groups = new EmbeddedContentSeedSource().GetGroups();

        Assert.Equal(["shared.contact", "shared.footer", "shared.social"], groups.Select(g => g.Name));
        Assert.All(groups, g => Assert.True(g.Version >= 1));
    }

    [Fact]
    public void ShippedGroups_HaveBothLanguagesOnEveryValue()
    {
        foreach (var group in new EmbeddedContentSeedSource().GetGroups())
        {
            var fields = group.Fields.Concat(group.Lists.SelectMany(l => l.Items).SelectMany(i => i.Fields));
            foreach (var field in fields)
            {
                Assert.True(
                    field.Values.ContainsKey("el") && field.Values.ContainsKey("en"),
                    $"{group.Name}: {field.Key} needs el and en.");
                Assert.All(field.Values.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
            }
        }
    }

    [Fact]
    public async Task ShippedGroups_ApplyCleanlyToAFreshDatabase()
    {
        var db = new ContentSeedTestDatabase(_connectionString);

        await db.RunSeederAsync(new EmbeddedContentSeedSource());

        var groups = new EmbeddedContentSeedSource().GetGroups();
        Assert.Equal(groups.Count, (await db.MarkersAsync()).Count);
        var items = await db.ItemsAsync();
        Assert.Equal(groups.SelectMany(g => g.Fields).Sum(f => f.Values.Count), items.Count);
        var phone = items.Single(i => i.FieldKey == "phone" && i.Locale == "el");
        Assert.True(ContentLink.TryParse(phone.Value, out var link));
        Assert.Equal("tel:+306940057799", link.Url);
        var instagram = items.Single(i => i.FieldKey == "instagram" && i.Locale == "el");
        Assert.Contains("Η Saronida Sailing στο Instagram", instagram.Value);
    }

    [Fact]
    public void Parse_ReadsFieldsListsAndLinks()
    {
        var group = EmbeddedContentSeedSource.Parse(
            "home.json",
            """
            {
              "group": "home",
              "version": 3,
              "fields": { "home.hero.title": { "type": "text", "el": "Α", "en": "A" } },
              "lists": {
                "home.trips": [
                  { "title": { "type": "text", "el": "Β" }, "link": { "type": "link", "url": "https://example.com/", "el": "Δες" } }
                ]
              }
            }
            """);

        Assert.Equal(("home", 3), (group.Name, group.Version));
        var field = Assert.Single(group.Fields);
        Assert.Equal(ContentType.Text, field.Type);
        Assert.Equal("A", field.Values["en"]);
        var item = Assert.Single(Assert.Single(group.Lists).Items);
        Assert.Equal(["title", "link"], item.Fields.Select(f => f.Key));
        Assert.True(ContentLink.TryParse(item.Fields[1].Values["el"], out var link));
        Assert.Equal(("https://example.com/", "Δες"), (link.Url, link.Label));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "group": "x", "version": 1 }""")]
    [InlineData("""{ "group": "shared.footer", "version": 0 }""")]
    [InlineData("""{ "group": "shared.other", "version": 1 }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "extra": 1 }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.contact.phone": { "type": "text", "el": "x" } } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.footerx.a": { "type": "text", "el": "x" } } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "lists": { "home.trips": [] } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.footer.a": { "type": "bold", "el": "x" } } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.footer.a": { "type": "image", "el": "x" } } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.footer.a": { "el": "x" } } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.footer.a": { "type": "text" } } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.footer.a": { "type": "text", "fr": "x" } } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.footer.a": { "type": "link", "el": "x" } } }""")]
    [InlineData("""{ "group": "shared.footer", "version": 1, "fields": { "shared.footer.a": { "type": "text", "url": "https://a.b/", "el": "x" } } }""")]
    public void Parse_InvalidFile_ThrowsNamingTheFile(string json)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EmbeddedContentSeedSource.Parse("shared.footer.json", json));

        Assert.Contains("shared.footer.json", ex.Message);
    }

    [Fact]
    public void Parse_PageGroup_MayHoldAnyKeyOfThePage()
    {
        var group = EmbeddedContentSeedSource.Parse(
            "home.json",
            """{ "group": "home", "version": 1, "fields": { "home.seo.title": { "type": "text", "el": "x" } } }""");

        Assert.Equal("home.seo.title", Assert.Single(group.Fields).Key);
    }
}
