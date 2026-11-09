using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Tests;

public sealed class ContentServiceTests : IDisposable
{
    private const string Email = "admin@example.com";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingInvalidator _invalidator = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly ContentEditor _editor = new(Email, "10.0.0.1");

    public ContentServiceTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.Migrate();
    }

    [Fact]
    public async Task Read_ReturnsRequestedLocaleValue()
    {
        await Apply(
            Field("home.hero.title", "el", "Γεια"),
            Field("home.hero.title", "en", "Hello"));

        var el = await Read("home", "el");
        var en = await Read("home", "en");

        Assert.Equal(new ContentValue("Γεια", ContentType.Text, false), el.GetField("home.hero.title"));
        Assert.Equal(new ContentValue("Hello", ContentType.Text, false), en.GetField("home.hero.title"));
        Assert.Null(en.GetField("home.hero.missing"));
    }

    [Fact]
    public async Task Read_MissingEnglish_FallsBackToGreek()
    {
        await Apply(Field("home.hero.title", "el", "Γεια"));

        var en = await Read("home", "en");

        Assert.Equal(new ContentValue("Γεια", ContentType.Text, true), en.GetField("home.hero.title"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Read_EmptyEnglish_FallsBackToGreek(string english)
    {
        await Apply(Field("home.hero.title", "el", "Γεια"), Field("home.hero.title", "en", english));

        var en = await Read("home", "en");

        Assert.Equal(new ContentValue("Γεια", ContentType.Text, true), en.GetField("home.hero.title"));
    }

    [Fact]
    public async Task Read_IncludesSharedAndExcludesOtherPages()
    {
        await Apply(
            Field("home.hero.title", "el", "home"),
            Field("shared.footer.phone-1", "el", "123"),
            Field("boat.hero.title", "el", "boat"));

        var home = await Read("home", "el");

        Assert.Equal("123", home.GetField("shared.footer.phone-1")?.Value);
        Assert.Equal("home", home.GetField("home.hero.title")?.Value);
        Assert.Null(home.GetField("boat.hero.title"));
    }

    [Fact]
    public async Task Read_ListsAreOrderedAndIncludeInactiveEntries()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        await Apply(
            new AddListItem("home.highlights", a),
            new AddListItem("home.highlights", b),
            new AddListItem("home.highlights", c),
            new ReorderList("home.highlights", [c, a, b]),
            new SetListItemActive("home.highlights", a, false),
            Field("home.highlights.title", "el", "A", a),
            Field("home.highlights.title", "el", "C", c));

        var page = await Read("home", "el");
        var list = page.GetList("home.highlights");

        Assert.Equal([c, a, b], list.Select(e => e.Id));
        Assert.Equal([0, 1, 2], list.Select(e => e.SortOrder));
        Assert.Equal([true, false, true], list.Select(e => e.IsActive));
        Assert.Equal("A", list[1].Fields["title"].Value);
        Assert.Empty(list[2].Fields);
        Assert.Empty(page.GetList("home.unknown"));
    }

    [Fact]
    public async Task Read_ListFieldsFallBackToGreek()
    {
        var id = Guid.NewGuid();
        await Apply(
            new AddListItem("home.highlights", id),
            Field("home.highlights.title", "el", "Τίτλος", id),
            Field("home.highlights.title", "en", "", id));

        var en = await Read("home", "en");

        Assert.Equal(new ContentValue("Τίτλος", ContentType.Text, true), en.GetList("home.highlights")[0].Fields["title"]);
    }

    [Fact]
    public async Task Read_SectionIsActiveByDefaultAndInactiveWhenRowSaysSo()
    {
        await Apply(new SetSectionActive("home.instagram", false));

        var page = await Read("home", "el");

        Assert.True(page.IsSectionActive("home.hero"));
        Assert.False(page.IsSectionActive("home.instagram"));
    }

    [Fact]
    public async Task Read_InvalidPageKeyOrLocale_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Read("Home", "el"));
        await Assert.ThrowsAsync<ArgumentException>(() => Read("home", "de"));
    }

    [Fact]
    public async Task Write_NewField_CreatesItemAndOneHistoryRow()
    {
        var result = await Apply(Field("home.hero.title", "el", "Hello"));

        Assert.True(result.Succeeded);
        await using var verify = CreateContext();
        var item = await verify.Set<ContentItem>().SingleAsync();
        var entry = await verify.Set<ContentHistory>().SingleAsync();
        Assert.Equal(("home", "hero", "title", "el", "Hello", Email), (item.PageKey, item.SectionKey, item.FieldKey, item.Locale, item.Value, item.UpdatedBy));
        Assert.Equal((item.Id, "Hello", Email), (entry.ContentItemId, entry.Value, entry.ChangedBy));
    }

    [Fact]
    public async Task Write_SameValueAgain_WritesNothingAndDoesNotEvict()
    {
        await Apply(Field("home.hero.title", "el", "Hello"));
        _invalidator.Calls.Clear();
        _audit.Entries.Clear();

        var result = await Apply(Field("home.hero.title", "el", "Hello"));

        Assert.True(result.Succeeded);
        await using var verify = CreateContext();
        Assert.Single(await verify.Set<ContentHistory>().ToListAsync());
        Assert.Empty(_invalidator.Calls);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Write_ChangedValue_UpdatesItemAndAddsSecondHistoryRow()
    {
        await Apply(Field("home.hero.title", "el", "Hello"));
        _time.Advance(TimeSpan.FromMinutes(1));

        await Apply([Field("home.hero.title", "el", "Hi")], new ContentEditor("other@example.com", null));

        await using var verify = CreateContext();
        var item = await verify.Set<ContentItem>().SingleAsync();
        var history = await new ContentHistoryRepository(verify).GetForItemAsync(item.Id);
        Assert.Equal(("Hi", "other@example.com"), (item.Value, item.UpdatedBy));
        Assert.Equal(["Hi", "Hello"], history.Select(h => h.Value));
        Assert.Equal(["other@example.com", Email], history.Select(h => h.ChangedBy));
    }

    [Fact]
    public async Task Write_TypeMismatch_FailsWritesNothingAndAuditsRejection()
    {
        await Apply(Field("home.hero.title", "el", "Hello"));
        _invalidator.Calls.Clear();
        _audit.Entries.Clear();

        var result = await Apply(new SetField("home.hero.title", "el", ContentType.RichText, "<p>x</p>"));

        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
        await using var verify = CreateContext();
        Assert.Equal("Hello", (await verify.Set<ContentItem>().SingleAsync()).Value);
        Assert.Single(await verify.Set<ContentHistory>().ToListAsync());
        Assert.Empty(_invalidator.Calls);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("content-save-rejected", Email, "10.0.0.1"), (entry.Event, entry.User, entry.Ip));
        Assert.Contains("errors=1", entry.Details);
    }

    [Fact]
    public async Task Write_OneInvalidChangeAmongValidOnes_WritesNothing()
    {
        await Apply(Field("home.hero.title", "el", "Hello"));
        var listId = Guid.NewGuid();
        _invalidator.Calls.Clear();

        var result = await Apply(
            Field("home.hero.title", "el", "Changed"),
            Field("home.hero.subtitle", "el", "Sub"),
            new AddListItem("home.highlights", listId),
            new SetSectionActive("home.instagram", false),
            Field("Home.hero.bad", "el", "x"));

        Assert.False(result.Succeeded);
        await using var verify = CreateContext();
        Assert.Equal("Hello", (await verify.Set<ContentItem>().SingleAsync()).Value);
        Assert.Single(await verify.Set<ContentHistory>().ToListAsync());
        Assert.Empty(await verify.Set<ContentListItem>().ToListAsync());
        Assert.Empty(await verify.Set<ContentSection>().ToListAsync());
        Assert.Empty(_invalidator.Calls);
    }

    [Fact]
    public async Task Write_RejectionDiscardsStagedChangesInTheScope()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.ApplyChangesAsync(
            [Field("home.hero.title", "el", "Hello"), Field("bad", "el", "x")], _editor);

        Assert.False(result.Succeeded);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Write_AddListItemSetFieldAndReorderInOneBatch_PersistsEverything()
    {
        var existing = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", existing));
        var added = Guid.NewGuid();

        var result = await Apply(
            new AddListItem("home.highlights", added),
            Field("home.highlights.title", "el", "first", added),
            Field("home.highlights.title", "el", "second", added),
            new ReorderList("home.highlights", [added, existing]));

        Assert.True(result.Succeeded);
        await using var verify = CreateContext();
        var lists = await verify.Set<ContentListItem>().OrderBy(l => l.SortOrder).ToListAsync();
        Assert.Equal([added, existing], lists.Select(l => l.Id));
        Assert.Equal([0, 1], lists.Select(l => l.SortOrder));
        var field = await verify.Set<ContentItem>().SingleAsync();
        Assert.Equal(("second", added, "highlights"), (field.Value, field.ListItemId, field.SectionKey));
        var history = await verify.Set<ContentHistory>().Where(h => h.ContentItemId == field.Id).ToListAsync();
        Assert.Equal(["first", "second"], history.Select(h => h.Value).Order());
    }

    [Fact]
    public async Task Write_NewItemsAreAppendedAtTheEnd()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", first), new AddListItem("home.highlights", second));

        await using var verify = CreateContext();
        var lists = await verify.Set<ContentListItem>().OrderBy(l => l.SortOrder).ToListAsync();
        Assert.Equal([first, second], lists.Select(l => l.Id));
        Assert.Equal([0, 1], lists.Select(l => l.SortOrder));
    }

    [Fact]
    public async Task Write_AddListItemWithExistingOrEmptyId_Fails()
    {
        var id = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", id));

        var existing = await Apply(new AddListItem("home.highlights", id));
        var empty = await Apply(new AddListItem("home.highlights", Guid.Empty));
        var twiceInBatch = await Apply(
            new AddListItem("home.highlights", Guid.NewGuid()),
            new AddListItem("home.highlights", id));

        Assert.False(existing.Succeeded);
        Assert.False(empty.Succeeded);
        Assert.False(twiceInBatch.Succeeded);
        await using var verify = CreateContext();
        Assert.Single(await verify.Set<ContentListItem>().ToListAsync());
    }

    [Fact]
    public async Task Write_AddListItemTwiceWithSameIdInOneBatch_Fails()
    {
        var id = Guid.NewGuid();

        var result = await Apply(new AddListItem("home.highlights", id), new AddListItem("home.highlights", id));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Write_DeleteListItem_CascadesToFieldsAndHistory()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        await Apply(
            new AddListItem("home.highlights", id),
            new AddListItem("home.highlights", other),
            Field("home.highlights.title", "el", "x", id),
            Field("home.highlights.title", "el", "y", other));

        var result = await Apply(new DeleteListItem("home.highlights", id));

        Assert.True(result.Succeeded);
        await using var verify = CreateContext();
        Assert.Equal(other, (await verify.Set<ContentListItem>().SingleAsync()).Id);
        Assert.Equal(other, (await verify.Set<ContentItem>().SingleAsync()).ListItemId);
        Assert.Single(await verify.Set<ContentHistory>().ToListAsync());
    }

    [Fact]
    public async Task Write_EditAndDeleteSameStoredItemInOneBatch_Succeeds()
    {
        var id = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", id), Field("home.highlights.title", "el", "x", id));

        var result = await Apply(
            Field("home.highlights.title", "el", "changed", id),
            Field("home.highlights.subtitle", "el", "new", id),
            new DeleteListItem("home.highlights", id));

        Assert.True(result.Succeeded);
        await using var verify = CreateContext();
        Assert.Empty(await verify.Set<ContentListItem>().ToListAsync());
        Assert.Empty(await verify.Set<ContentItem>().ToListAsync());
        Assert.Empty(await verify.Set<ContentHistory>().ToListAsync());
    }

    [Fact]
    public async Task Write_AddEditAndDeleteSameItemInOneBatch_PersistsNothingForIt()
    {
        var kept = Guid.NewGuid();
        var temp = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", kept));

        var result = await Apply(
            new AddListItem("home.highlights", temp),
            Field("home.highlights.title", "el", "x", temp),
            new DeleteListItem("home.highlights", temp));

        Assert.True(result.Succeeded);
        await using var verify = CreateContext();
        Assert.Equal(kept, (await verify.Set<ContentListItem>().SingleAsync()).Id);
        Assert.Empty(await verify.Set<ContentItem>().ToListAsync());
        Assert.Empty(await verify.Set<ContentHistory>().ToListAsync());
    }

    [Fact]
    public async Task Write_DeleteUnknownOrForeignListItem_Fails()
    {
        var id = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", id));

        var unknown = await Apply(new DeleteListItem("home.highlights", Guid.NewGuid()));
        var foreign = await Apply(new DeleteListItem("home.trips", id));

        Assert.False(unknown.Succeeded);
        Assert.False(foreign.Succeeded);
    }

    [Fact]
    public async Task Write_ReorderWithMissingExtraOrDuplicateId_Fails()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", a), new AddListItem("home.highlights", b));

        var missing = await Apply(new ReorderList("home.highlights", [a]));
        var extra = await Apply(new ReorderList("home.highlights", [a, b, Guid.NewGuid()]));
        var duplicate = await Apply(new ReorderList("home.highlights", [a, a]));
        var unknown = await Apply(new ReorderList("home.highlights", [a, Guid.NewGuid()]));

        Assert.False(missing.Succeeded);
        Assert.False(extra.Succeeded);
        Assert.False(duplicate.Succeeded);
        Assert.False(unknown.Succeeded);
    }

    [Fact]
    public async Task Write_ReorderAfterDeleteInSameBatch_UsesRemainingItems()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        await Apply(
            new AddListItem("home.highlights", a),
            new AddListItem("home.highlights", b),
            new AddListItem("home.highlights", c));

        var result = await Apply(new DeleteListItem("home.highlights", b), new ReorderList("home.highlights", [c, a]));

        Assert.True(result.Succeeded);
        await using var verify = CreateContext();
        var lists = await verify.Set<ContentListItem>().OrderBy(l => l.SortOrder).ToListAsync();
        Assert.Equal([c, a], lists.Select(l => l.Id));
    }

    [Fact]
    public async Task Write_SetFieldOnItemOfAnotherList_Fails()
    {
        var id = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", id));

        var result = await Apply(Field("home.trips.title", "el", "x", id));
        var otherPage = await Apply(Field("boat.highlights.title", "el", "x", id));

        Assert.False(result.Succeeded);
        Assert.False(otherPage.Succeeded);
    }

    [Fact]
    public async Task Write_SetFieldOnUnknownListItem_Fails()
    {
        var result = await Apply(Field("home.highlights.title", "el", "x", Guid.NewGuid()));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Write_SetFieldOnItemDeletedEarlierInBatch_Fails()
    {
        var id = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", id));

        var result = await Apply(
            new DeleteListItem("home.highlights", id),
            Field("home.highlights.title", "el", "x", id));

        Assert.False(result.Succeeded);
        await using var verify = CreateContext();
        Assert.Single(await verify.Set<ContentListItem>().ToListAsync());
    }

    [Fact]
    public async Task Write_SetListItemActive_TogglesTheFlag()
    {
        var id = Guid.NewGuid();
        await Apply(new AddListItem("home.highlights", id));

        var off = await Apply(new SetListItemActive("home.highlights", id, false));
        await using (var verify = CreateContext())
        {
            Assert.False((await verify.Set<ContentListItem>().SingleAsync()).IsActive);
        }

        var on = await Apply(new SetListItemActive("home.highlights", id, true));
        await using (var verify = CreateContext())
        {
            Assert.True((await verify.Set<ContentListItem>().SingleAsync()).IsActive);
        }

        Assert.True(off.Succeeded && on.Succeeded);
        Assert.False((await Apply(new SetListItemActive("home.trips", id, false))).Succeeded);
    }

    [Fact]
    public async Task Write_SetSectionActive_CreatesThenUpdates()
    {
        await Apply(new SetSectionActive("home.instagram", false));
        await using (var verify = CreateContext())
        {
            var section = await verify.Set<ContentSection>().SingleAsync();
            Assert.Equal(("home", "instagram", false), (section.PageKey, section.SectionKey, section.IsActive));
        }

        await Apply(new SetSectionActive("home.instagram", true));
        await using (var verify = CreateContext())
        {
            Assert.True((await verify.Set<ContentSection>().SingleAsync()).IsActive);
        }
    }

    [Theory]
    [InlineData("Home.hero.title")]
    [InlineData("home.hero")]
    [InlineData("home.hero.title.extra")]
    [InlineData("home..title")]
    [InlineData("home.hero.-title")]
    [InlineData("home.hero.ti_tle")]
    [InlineData("")]
    public async Task Write_BadFieldKey_Fails(string key)
    {
        var result = await Apply(Field(key, "el", "x"));

        Assert.False(result.Succeeded);
        await using var verify = CreateContext();
        Assert.Empty(await verify.Set<ContentItem>().ToListAsync());
    }

    [Theory]
    [InlineData("Home.highlights")]
    [InlineData("home")]
    [InlineData("home.highlights.title")]
    [InlineData(".highlights")]
    public async Task Write_BadListOrSectionKey_Fails(string key)
    {
        var id = Guid.NewGuid();

        Assert.False((await Apply(new AddListItem(key, id))).Succeeded);
        Assert.False((await Apply(new DeleteListItem(key, id))).Succeeded);
        Assert.False((await Apply(new ReorderList(key, [id]))).Succeeded);
        Assert.False((await Apply(new SetListItemActive(key, id, false))).Succeeded);
        Assert.False((await Apply(new SetSectionActive(key, false))).Succeeded);
    }

    [Fact]
    public async Task Write_UnsupportedLocaleOrType_Fails()
    {
        Assert.False((await Apply(Field("home.hero.title", "de", "x"))).Succeeded);
        Assert.False((await Apply(Field("home.hero.title", "EL", "x"))).Succeeded);
        Assert.False((await Apply(new SetField("home.hero.title", "el", (ContentType)99, "x"))).Succeeded);
    }

    [Fact]
    public async Task Write_OverlongSegment_Fails()
    {
        var result = await Apply(Field($"home.{new string('a', 101)}.title", "el", "x"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Write_BlankEditorEmail_Fails()
    {
        var result = await Apply([Field("home.hero.title", "el", "x")], new ContentEditor(" ", null));

        Assert.False(result.Succeeded);
        await using var verify = CreateContext();
        Assert.Empty(await verify.Set<ContentItem>().ToListAsync());
    }

    [Fact]
    public async Task Write_EmptyBatch_SucceedsAndDoesNothing()
    {
        var result = await Apply();

        Assert.True(result.Succeeded);
        Assert.Empty(_invalidator.Calls);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Write_Success_EvictsOnceWithDistinctTouchedPagesAndAuditsOnce()
    {
        var id = Guid.NewGuid();

        await Apply(
            Field("home.hero.title", "el", "a"),
            Field("home.hero.subtitle", "el", "b"),
            Field("shared.footer.phone-1", "el", "1"),
            new AddListItem("home.highlights", id),
            Field("home.hero.title", "el", "a"),
            new SetSectionActive("boat.specs", true));

        var call = Assert.Single(_invalidator.Calls);
        Assert.Equal(["home", "shared"], call);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("content-saved", Email, "10.0.0.1"), (entry.Event, entry.User, entry.Ip));
        Assert.Equal(
            "pages=home,shared fields=3 list-added=1 list-deleted=0 reordered=0 list-activated=0 sections=0",
            entry.Details);
    }

    [Fact]
    public async Task Write_EvictionHappensAfterCommit()
    {
        await using var context = CreateContext();
        var observed = -1;
        _invalidator.OnEvict = () =>
        {
            using var other = CreateContext();
            observed = other.Set<ContentItem>().Count();
        };

        await CreateService(context).ApplyChangesAsync([Field("home.hero.title", "el", "a")], _editor);

        Assert.Equal(1, observed);
    }

    public void Dispose() => _connection.Dispose();

    private static SetField Field(string key, string locale, string value, Guid? listItemId = null) =>
        new(key, locale, ContentType.Text, value, listItemId);

    private Task<ContentSaveResult> Apply(params ContentChange[] changes) => Apply(changes, _editor);

    private async Task<ContentSaveResult> Apply(ContentChange[] changes, ContentEditor editor)
    {
        await using var context = CreateContext();
        return await CreateService(context).ApplyChangesAsync(changes, editor);
    }

    private async Task<PageContent> Read(string pageKey, string locale)
    {
        await using var context = CreateContext();
        return await CreateService(context).GetPageContentAsync(pageKey, locale);
    }

    private ContentService CreateService(AppDbContext context) =>
        new(
            new ContentItemRepository(context),
            new ContentListItemRepository(context),
            new ContentSectionRepository(context),
            new ContentHistoryRepository(context),
            new UnitOfWork(context),
            _invalidator,
            _audit);

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _time);

    private sealed class RecordingInvalidator : IPageCacheInvalidator
    {
        public List<string[]> Calls { get; } = [];

        public Action? OnEvict { get; set; }

        public Task EvictPagesAsync(IReadOnlyCollection<string> pageKeys, CancellationToken ct = default)
        {
            Calls.Add([.. pageKeys]);
            OnEvict?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<(string Event, string? User, string? Ip, string? Details)> Entries { get; } = [];

        public void Record(string eventName, string? userName, string? ipAddress, string? details = null) =>
            Entries.Add((eventName, userName, ipAddress, details));
    }
}
