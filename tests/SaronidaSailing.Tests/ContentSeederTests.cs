using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Tests;

public sealed class ContentSeederTests : IAsyncLifetime
{
    private string _connectionString = "";
    private ContentSeedTestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        _db = new ContentSeedTestDatabase(_connectionString);
    }

    public Task DisposeAsync() => PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));

    private static SeedField Text(string key, string el, string? en = null)
    {
        var values = new Dictionary<string, string> { ["el"] = el };
        if (en is not null)
        {
            values["en"] = en;
        }

        return new SeedField(key, ContentType.Text, values);
    }

    private static ContentSeedGroup Group(
        string name, int version, SeedField[] fields, params SeedList[] lists) => new(name, version, fields, lists);

    private static SeedList Trips(params string[] titles) => new(
        "home.trips",
        [.. titles.Select(t => new SeedListItem([Text("title", t, t + " EN")]))]);

    private static ContentSeedGroup Home(int version = 1) => Group(
        "home",
        version,
        [Text("home.hero.title", "Τίτλος", "Title"), Text("home.hero.lead", "Εισαγωγή", "Lead")],
        Trips("Πρώτη", "Δεύτερη"));

    private async Task<string?> FieldValue(string key, string locale, Guid? listItemId = null)
    {
        var parts = key.Split('.');
        return (await _db.ItemsAsync()).SingleOrDefault(i =>
            i.PageKey == parts[0] && i.SectionKey == parts[1] && i.FieldKey == parts[2]
            && i.Locale == locale && i.ListItemId == listItemId)?.Value;
    }

    [Fact]
    public async Task EmptyDatabase_WritesFieldsAndListsInBothLocales_AndStoresTheMarker()
    {
        var distributedLock = new RecordingLock();

        await _db.RunSeederAsync(new FakeSeedSource(Home()), distributedLock);

        Assert.Equal("Τίτλος", await FieldValue("home.hero.title", "el"));
        Assert.Equal("Title", await FieldValue("home.hero.title", "en"));
        var listItems = await _db.ListItemsAsync();
        Assert.Equal(2, listItems.Count);
        Assert.Equal([0, 1], listItems.Select(l => l.SortOrder));
        var items = await _db.ItemsAsync();
        Assert.Equal(4 + 4, items.Count);
        var first = listItems[0];
        Assert.Equal("Πρώτη", await FieldValue("home.trips.title", "el", first.Id));
        Assert.Equal("Δεύτερη EN", await FieldValue("home.trips.title", "en", listItems[1].Id));
        Assert.All(items, i => Assert.Equal("seed", i.UpdatedBy));
        Assert.Equal(items.Count, await _db.HistoryCountAsync());
        var marker = Assert.Single(await _db.MarkersAsync());
        Assert.Equal("seed.home", marker.Key);
        Assert.Equal("1", marker.Value);
        Assert.Equal([["home"]], _db.Invalidator.Calls);
        Assert.Equal([(ContentSeeder.LockResource, TimeSpan.FromMinutes(2))], distributedLock.Acquired);
        Assert.Equal(1, distributedLock.Released);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing()
    {
        await _db.RunSeederAsync(new FakeSeedSource(Home()));
        var history = await _db.HistoryCountAsync();
        var items = await _db.ItemsAsync();

        await _db.RunSeederAsync(new FakeSeedSource(Home()));

        Assert.Equal(history, await _db.HistoryCountAsync());
        Assert.Equal(items.Count, (await _db.ItemsAsync()).Count);
        Assert.Equal(2, (await _db.ListItemsAsync()).Count);
        Assert.Single(await _db.MarkersAsync());
    }

    [Fact]
    public async Task ExistingValues_AreNotOverwritten_AndMissingSiblingsAreFilled()
    {
        await _db.ApplyAsync(
            new SetField("home.hero.title", "el", ContentType.Text, "Δικό μου"),
            new SetField("home.hero.lead", "en", ContentType.Text, ""));

        await _db.RunSeederAsync(new FakeSeedSource(Home()));

        Assert.Equal("Δικό μου", await FieldValue("home.hero.title", "el"));
        Assert.Equal("Title", await FieldValue("home.hero.title", "en"));
        Assert.Equal("Εισαγωγή", await FieldValue("home.hero.lead", "el"));
        Assert.Equal("", await FieldValue("home.hero.lead", "en"));
    }

    [Fact]
    public async Task ListWithItems_IsLeftAlone()
    {
        var mine = Guid.NewGuid();
        await _db.ApplyAsync(
            new AddListItem("home.trips", mine),
            new SetField("home.trips.title", "el", ContentType.Text, "Δική μου", mine));

        await _db.RunSeederAsync(new FakeSeedSource(Home()));

        var listItems = await _db.ListItemsAsync();
        Assert.Equal(mine, Assert.Single(listItems).Id);
        Assert.Null(await FieldValue("home.trips.title", "en", mine));
        Assert.Equal("Τίτλος", await FieldValue("home.hero.title", "el"));
    }

    [Fact]
    public async Task SameVersionMarker_WritesNothing_EvenWhenAFieldWasDeleted()
    {
        await _db.RunSeederAsync(new FakeSeedSource(Home()));
        await using (var context = _db.CreateContext())
        {
            var row = context.Set<ContentItem>().Single(i => i.FieldKey == "lead" && i.Locale == "en");
            context.Remove(row);
            await context.SaveChangesAsync();
        }

        await _db.RunSeederAsync(new FakeSeedSource(Home()));

        Assert.Null(await FieldValue("home.hero.lead", "en"));
    }

    [Fact]
    public async Task HigherVersion_FillsOnlyMissingValues()
    {
        await _db.RunSeederAsync(new FakeSeedSource(Home()));
        await _db.ApplyAsync(new SetField("home.hero.title", "el", ContentType.Text, "Αλλαγμένο"));
        await using (var context = _db.CreateContext())
        {
            context.Remove(context.Set<ContentItem>().Single(i => i.FieldKey == "lead" && i.Locale == "en"));
            await context.SaveChangesAsync();
        }

        var next = Group(
            "home", 2,
            [Text("home.hero.title", "Τίτλος", "Title"), Text("home.hero.lead", "Εισαγωγή", "Lead"), Text("home.hero.extra", "Νέο", "New")],
            Trips("Πρώτη", "Δεύτερη"));
        await _db.RunSeederAsync(new FakeSeedSource(next));

        Assert.Equal("Αλλαγμένο", await FieldValue("home.hero.title", "el"));
        Assert.Equal("Lead", await FieldValue("home.hero.lead", "en"));
        Assert.Equal("Νέο", await FieldValue("home.hero.extra", "el"));
        Assert.Equal(2, (await _db.ListItemsAsync()).Count);
        Assert.Equal("2", Assert.Single(await _db.MarkersAsync()).Value);
    }

    [Fact]
    public async Task InvalidSeed_ThrowsNamingTheGroup_AndWritesNothing()
    {
        var group = Group(
            "shared.social", 1,
            [
                Text("shared.social.note", "Εντάξει", "Fine"),
                new SeedField(
                    "shared.social.instagram", ContentType.Link,
                    new Dictionary<string, string> { ["el"] = new ContentLink("http://example.com", "Label").ToJson() })
            ]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _db.RunSeederAsync(new FakeSeedSource(group)));

        Assert.Contains("shared.social", ex.Message);
        Assert.Contains("shared.social.instagram", ex.Message);
        Assert.Empty(await _db.ItemsAsync());
        Assert.Empty(await _db.MarkersAsync());
    }

    [Fact]
    public async Task GroupsAreSeededIndependently_AndOneLogLinePerAppliedGroup()
    {
        var logger = new RecordingLogger<ContentSeeder>();
        var footer = Group("shared.footer", 1, [Text("shared.footer.tagline", "Α", "A")]);

        await _db.RunSeederAsync(new FakeSeedSource(footer, Home()), logger: logger);
        await _db.RunSeederAsync(new FakeSeedSource(footer, Home()), logger: logger);

        Assert.Equal(2, (await _db.MarkersAsync()).Count);
        var lines = logger.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message).ToList();
        Assert.Equal(
            [
                "Seeded content group shared.footer v1: 2 field value(s), 0 list(s).",
                "Seeded content group home v1: 8 field value(s), 1 list(s)."
            ],
            lines);
        Assert.DoesNotContain(lines, l => l.Contains("Τίτλος"));
    }

    [Fact]
    public async Task LockFailure_StopsTheSeed_AndWritesNothing()
    {
        var distributedLock = new RecordingLock { Failure = new TimeoutException("lock") };

        await Assert.ThrowsAsync<TimeoutException>(() => _db.RunSeederAsync(new FakeSeedSource(Home()), distributedLock));

        Assert.Empty(await _db.ItemsAsync());
        Assert.Empty(await _db.MarkersAsync());
    }

    [Fact]
    public async Task NoGroups_TakesNoLock()
    {
        var distributedLock = new RecordingLock();

        await _db.RunSeederAsync(new FakeSeedSource(), distributedLock);

        Assert.Empty(distributedLock.Acquired);
    }
}
