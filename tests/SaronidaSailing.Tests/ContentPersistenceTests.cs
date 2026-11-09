using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Tests;

public sealed class ContentPersistenceTests : IDisposable
{
    private const string Editor = "admin@example.com";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));

    public ContentPersistenceTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.Migrate();
    }

    [Fact]
    public async Task StandaloneItems_DuplicateKeySameLocale_Throws()
    {
        await using var context = CreateContext();
        context.Add(Standalone("el"));
        context.Add(Standalone("el"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task StandaloneItems_SameKeyDifferentLocale_Succeeds()
    {
        await using var context = CreateContext();
        context.Add(Standalone("el"));
        context.Add(Standalone("en"));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Set<ContentItem>().CountAsync());
    }

    [Fact]
    public async Task ListFields_SameFieldAndLocaleOnSameListItem_Throws()
    {
        await using var context = CreateContext();
        var list = ContentListItem.Create("home", "highlights", 0);
        context.Add(list);
        context.Add(ListField(list.Id, "title", "el"));
        context.Add(ListField(list.Id, "title", "el"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ListFields_SameFieldAndLocaleOnDifferentListItems_Succeeds()
    {
        await using var context = CreateContext();
        var first = ContentListItem.Create("home", "highlights", 0);
        var second = ContentListItem.Create("home", "highlights", 1);
        context.AddRange(first, second);
        context.Add(ListField(first.Id, "title", "el"));
        context.Add(ListField(second.Id, "title", "el"));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Set<ContentItem>().CountAsync());
    }

    [Fact]
    public async Task StandaloneAndListField_SharingKeyParts_Succeeds()
    {
        await using var context = CreateContext();
        var list = ContentListItem.Create("home", "highlights", 0);
        context.Add(list);
        context.Add(Standalone("el", section: "highlights", field: "title"));
        context.Add(ListField(list.Id, "title", "el"));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Set<ContentItem>().CountAsync());
    }

    [Fact]
    public async Task Sections_DuplicatePageAndSection_Throws()
    {
        await using var context = CreateContext();
        context.Add(ContentSection.Create("home", "hero"));
        context.Add(ContentSection.Create("home", "hero", isActive: false));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task RemovingListItem_CascadesToFieldsAndTheirHistory()
    {
        var list = ContentListItem.Create("home", "highlights", 0);
        var field = ListField(list.Id, "title", "el");
        await using (var context = CreateContext())
        {
            context.Add(list);
            context.Add(field);
            context.Add(new ContentHistory { ContentItemId = field.Id, Value = "old", ChangedBy = Editor });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var lists = new ContentListItemRepository(context);
            var unitOfWork = new UnitOfWork(context);
            var loaded = await lists.FindAsync(list.Id);
            lists.Remove(loaded!);
            await unitOfWork.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Empty(await verify.Set<ContentListItem>().ToListAsync());
        Assert.Empty(await verify.Set<ContentItem>().ToListAsync());
        Assert.Empty(await verify.Set<ContentHistory>().ToListAsync());
    }

    [Fact]
    public async Task Type_IsPersistedAsItsName()
    {
        await using (var context = CreateContext())
        {
            context.Add(Standalone("el", type: ContentType.RichText));
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var stored = await verify.Database.SqlQueryRaw<string>("SELECT \"Type\" AS \"Value\" FROM \"ContentItems\"").ToListAsync();

        Assert.Equal(["RichText"], stored);
    }

    [Fact]
    public async Task ItemReads_ReturnOnlyRequestedPagesAndLocales()
    {
        var list = ContentListItem.Create("home", "highlights", 0);
        await using (var context = CreateContext())
        {
            context.Add(list);
            context.Add(Standalone("el", page: "home"));
            context.Add(Standalone("en", page: "home"));
            context.Add(Standalone("el", page: "boat"));
            context.Add(Standalone("el", page: "contact"));
            context.Add(ListField(list.Id, "title", "el"));
            context.Add(ListField(list.Id, "title", "de"));
            await context.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var items = await new ContentItemRepository(read).GetForPagesAsync(["home", "boat"], ["el"]);

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal("el", i.Locale));
        Assert.All(items, i => Assert.Contains(i.PageKey, new[] { "home", "boat" }));
        Assert.Empty(read.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ItemFinds_ReturnTrackedItemsForMatchingKeys()
    {
        var list = ContentListItem.Create("home", "highlights", 0);
        await using (var context = CreateContext())
        {
            context.Add(list);
            context.Add(Standalone("el", field: "title"));
            context.Add(ListField(list.Id, "title", "el"));
            await context.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var items = new ContentItemRepository(read);
        var standalone = await items.FindStandaloneAsync("home", "hero", "title", "el");
        var listField = await items.FindListFieldAsync(list.Id, "title", "el");

        Assert.NotNull(standalone);
        Assert.Null(standalone.ListItemId);
        Assert.NotNull(listField);
        Assert.Equal(list.Id, listField.ListItemId);
        Assert.Null(await items.FindStandaloneAsync("home", "hero", "title", "en"));
        Assert.Equal(2, read.ChangeTracker.Entries().Count());
    }

    [Fact]
    public async Task ListItemReads_AreOrderedBySortOrder()
    {
        await using (var context = CreateContext())
        {
            context.Add(ContentListItem.Create("home", "highlights", 2));
            context.Add(ContentListItem.Create("home", "highlights", 0));
            context.Add(ContentListItem.Create("home", "highlights", 1));
            context.Add(ContentListItem.Create("home", "trips", 0));
            context.Add(ContentListItem.Create("boat", "gallery", 0));
            context.Add(ContentListItem.Create("contact", "faq", 0));
            await context.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var lists = new ContentListItemRepository(read);
        var forPages = await lists.GetForPagesAsync(["home", "boat"]);
        var highlights = await lists.GetListAsync("home", "highlights");

        Assert.Equal(
            [("boat", "gallery", 0), ("home", "highlights", 0), ("home", "highlights", 1), ("home", "highlights", 2), ("home", "trips", 0)],
            forPages.Select(l => (l.PageKey, l.ListKey, l.SortOrder)));
        Assert.Equal([0, 1, 2], highlights.Select(l => l.SortOrder));
        Assert.Equal(3, read.ChangeTracker.Entries().Count());
    }

    [Fact]
    public async Task SectionReads_ReturnRequestedPagesAndFindsTheSection()
    {
        await using (var context = CreateContext())
        {
            context.Add(ContentSection.Create("home", "hero"));
            context.Add(ContentSection.Create("home", "instagram", isActive: false));
            context.Add(ContentSection.Create("boat", "specs"));
            await context.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var sections = new ContentSectionRepository(read);
        var forPages = await sections.GetForPagesAsync(["home"]);
        var found = await sections.FindAsync("home", "instagram");

        Assert.Equal(2, forPages.Count);
        Assert.NotNull(found);
        Assert.False(found.IsActive);
        Assert.Null(await sections.FindAsync("home", "missing"));
        Assert.Single(read.ChangeTracker.Entries());
    }

    [Fact]
    public async Task HistoryReads_AreNewestFirst()
    {
        var item = Standalone("el");
        await using (var context = CreateContext())
        {
            context.Add(item);
            await context.SaveChangesAsync();
        }

        foreach (var value in new[] { "v1", "v2", "v3" })
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            await using var context = CreateContext();
            context.Add(new ContentHistory { ContentItemId = item.Id, Value = value, ChangedBy = Editor });
            await context.SaveChangesAsync();
        }

        _time.Advance(TimeSpan.FromMinutes(1));
        var same = _time.GetUtcNow();
        await using (var context = CreateContext())
        {
            context.Add(new ContentHistory { Id = Guid.CreateVersion7(same), ContentItemId = item.Id, Value = "v4a", ChangedBy = Editor });
            context.Add(new ContentHistory { Id = Guid.CreateVersion7(same.AddMilliseconds(1)), ContentItemId = item.Id, Value = "v4b", ChangedBy = Editor });
            await context.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var history = await new ContentHistoryRepository(read).GetForItemAsync(item.Id);

        Assert.Equal(["v4b", "v4a", "v3", "v2", "v1"], history.Select(h => h.Value));
        Assert.Empty(read.ChangeTracker.Entries());
    }

    [Fact]
    public async Task StagingThroughSeveralRepositories_PersistsOnOneUnitOfWorkSave()
    {
        var list = ContentListItem.Create("home", "highlights", 0);
        var field = ListField(list.Id, "title", "el");
        await using (var context = CreateContext())
        {
            var lists = new ContentListItemRepository(context);
            var items = new ContentItemRepository(context);
            var sections = new ContentSectionRepository(context);
            var history = new ContentHistoryRepository(context);
            var unitOfWork = new UnitOfWork(context);

            lists.Add(list);
            items.Add(field);
            sections.Add(ContentSection.Create("home", "highlights"));
            history.Add(new ContentHistory { ContentItemId = field.Id, Value = "first", ChangedBy = Editor });

            await using (var other = CreateContext())
            {
                Assert.Empty(await other.Set<ContentItem>().ToListAsync());
            }

            await unitOfWork.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Single(await verify.Set<ContentListItem>().ToListAsync());
        Assert.Single(await verify.Set<ContentItem>().ToListAsync());
        Assert.Single(await verify.Set<ContentSection>().ToListAsync());
        Assert.Single(await verify.Set<ContentHistory>().ToListAsync());
    }

    [Fact]
    public async Task SetValue_UpdatesValueAndEditor()
    {
        var item = Standalone("el");
        await using (var context = CreateContext())
        {
            context.Add(item);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var loaded = await new ContentItemRepository(context).FindStandaloneAsync("home", "hero", "title", "el");
            loaded!.SetValue("changed", "other@example.com");
            await new UnitOfWork(context).SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var stored = await verify.Set<ContentItem>().SingleAsync();
        Assert.Equal("changed", stored.Value);
        Assert.Equal("other@example.com", stored.UpdatedBy);
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _time);

    private static ContentItem Standalone(
        string locale,
        string page = "home",
        string section = "hero",
        string field = "title",
        ContentType type = ContentType.Text) =>
        ContentItem.Create(page, section, field, locale, null, type, "value", Editor);

    private static ContentItem ListField(Guid listItemId, string field, string locale) =>
        ContentItem.Create("home", "highlights", field, locale, listItemId, ContentType.Text, "value", Editor);
}
