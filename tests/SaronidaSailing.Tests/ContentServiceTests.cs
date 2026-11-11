using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Content;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Tests;

public sealed class ContentServiceTests : IAsyncLifetime
{
    private const string Email = "admin@example.com";

    private string _connectionString = "";
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingInvalidator _invalidator = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly ContentEditor _editor = new(Email, "10.0.0.1");

    public async Task InitializeAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    public Task DisposeAsync() => PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));

    [Fact]
    public async Task Read_ReturnsRequestedLocaleValue()
    {
        await Apply(
            Field("home.hero.title", "el", "Γεια"),
            Field("home.hero.title", "en", "Hello"));

        var el = await Read("home", "el");
        var en = await Read("home", "en");

        Assert.Equal(new ContentValue("Γεια", ContentType.Text, false), el.GetField("home.hero.title"));
        Assert.Equal(
            new ContentValue("Hello", ContentType.Text, false) { DefaultLocaleValue = "Γεια" }, en.GetField("home.hero.title"));
        Assert.Null(en.GetField("home.hero.missing"));
    }

    [Fact]
    public async Task Read_EnglishOnly_HasNoDefaultLocaleValue()
    {
        await Apply(Field("home.hero.title", "en", "Hello"));

        var en = await Read("home", "en");

        Assert.Null(en.GetField("home.hero.title")!.DefaultLocaleValue);
    }

    [Fact]
    public async Task Read_MissingEnglish_FallsBackToGreek()
    {
        await Apply(Field("home.hero.title", "el", "Γεια"));

        var en = await Read("home", "en");

        Assert.Equal(
            new ContentValue("Γεια", ContentType.Text, true) { DefaultLocaleValue = "Γεια" }, en.GetField("home.hero.title"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Read_EmptyEnglish_FallsBackToGreek(string english)
    {
        await Apply(Field("home.hero.title", "el", "Γεια"), Field("home.hero.title", "en", english));

        var en = await Read("home", "en");

        Assert.Equal(
            new ContentValue("Γεια", ContentType.Text, true) { DefaultLocaleValue = "Γεια" }, en.GetField("home.hero.title"));
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

        Assert.Equal(
            new ContentValue("Τίτλος", ContentType.Text, true) { DefaultLocaleValue = "Τίτλος" },
            en.GetList("home.highlights")[0].Fields["title"]);
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
        Assert.Equal(["second"], history.Select(h => h.Value));
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

        var result = await Apply(
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
            $"pages=home,shared fields=3 list-added=1 list-deleted=0 reordered=0 list-activated=0 sections=0 save={result.SaveId}",
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

    private static SetField Rich(string key, string value, Guid? listItemId = null) =>
        new(key, "el", ContentType.RichText, value, listItemId);

    private static SetField LinkField(string key, string value) => new(key, "el", ContentType.Link, value);

    private async Task<List<ContentHistory>> HistoryRows()
    {
        await using var context = CreateContext();
        return await context.Set<ContentHistory>().AsNoTracking().OrderBy(h => h.Id).ToListAsync();
    }

    [Fact]
    public async Task Write_RichText_IsStoredAndRecordedSanitized()
    {
        var result = await Apply(Rich("about.story.body", "<p>Hi <b>there</b></p><script>alert(1)</script>"));

        Assert.True(result.Succeeded);
        var page = await Read("about", "el");
        Assert.Equal("<p>Hi there</p>", page.GetField("about.story.body")?.Value);
        Assert.Equal(["<p>Hi there</p>"], (await HistoryRows()).Select(h => h.Value));
    }

    [Fact]
    public async Task Write_RichTextWhoseSanitizedFormEqualsStored_IsANoOp()
    {
        await Apply(Rich("about.story.body", "<p>Hi</p>"));
        _invalidator.Calls.Clear();

        var result = await Apply(Rich("about.story.body", "<p>Hi</p><script>x</script>"));

        Assert.True(result.Succeeded);
        Assert.Null(result.SaveId);
        Assert.Empty(_invalidator.Calls);
        Assert.Single(await HistoryRows());
    }

    [Fact]
    public async Task Write_EmptyEditorRichText_IsStoredAsEmptyString()
    {
        await Apply(Rich("about.story.body", "<p><br></p>"));

        var page = await Read("about", "el");
        Assert.Equal("", page.GetField("about.story.body")?.Value);
    }

    [Fact]
    public async Task Write_InvalidLink_RejectsTheWholeBatch()
    {
        var result = await Apply(
            Field("home.hero.title", "el", "a"),
            LinkField("home.hero.cta", """{"url":"http://example.com","label":"Go"}"""));

        Assert.False(result.Succeeded);
        Assert.Null(result.SaveId);
        Assert.Equal(
            ["Invalid link for 'home.hero.cta' (el): URL must be https:, mailto: or tel: and label is required."],
            result.Errors);
        Assert.Empty(await HistoryRows());
        Assert.Null((await Read("home", "el")).GetField("home.hero.title"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"url":"https://example.com","label":""}""")]
    [InlineData("""{"url":"javascript:alert(1)","label":"x"}""")]
    public async Task Write_MalformedLink_Fails(string value)
    {
        var result = await Apply(LinkField("home.hero.cta", value));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Write_ValidAndEmptyLinks_AreAllowed()
    {
        var link = """{"url":"https://example.com","label":"Go"}""";
        Assert.True((await Apply(LinkField("home.hero.cta", link))).Succeeded);
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await Apply(LinkField("home.hero.cta", ""))).Succeeded);

        Assert.Equal("", (await Read("home", "el")).GetField("home.hero.cta")?.Value);
        Assert.Equal(["", link], (await HistoryRows()).OrderByDescending(h => h.CreatedAt).Select(h => h.Value));
    }

    [Fact]
    public async Task Write_SameFieldTwiceInOneBatch_WritesOneHistoryRowWithTheLastValue()
    {
        var result = await Apply(
            Field("home.hero.title", "el", "first"),
            Field("home.hero.title", "el", "second"));

        Assert.True(result.Succeeded);
        Assert.Equal("second", (await Read("home", "el")).GetField("home.hero.title")?.Value);
        Assert.Equal(["second"], (await HistoryRows()).Select(h => h.Value));
        Assert.Contains("fields=1 ", Assert.Single(_audit.Entries).Details);
    }

    [Fact]
    public async Task Write_FieldChangedAndRestoredInOneBatch_WritesNothingForIt()
    {
        await Apply(Field("home.hero.title", "el", "A"), Field("home.hero.subtitle", "el", "S"));
        _invalidator.Calls.Clear();
        _audit.Entries.Clear();

        var result = await Apply(
            [Field("home.hero.title", "el", "B"), Field("home.hero.title", "el", "A")],
            new ContentEditor("other@example.com", null));

        Assert.True(result.Succeeded);
        Assert.Null(result.SaveId);
        Assert.Empty(_invalidator.Calls);
        Assert.Empty(_audit.Entries);
        Assert.Equal(2, (await HistoryRows()).Count);
        await using var verify = CreateContext();
        Assert.All(await verify.Set<ContentItem>().ToListAsync(), i => Assert.Equal(Email, i.UpdatedBy));
    }

    [Fact]
    public async Task Write_RestoredFieldDoesNotHideOtherChanges()
    {
        await Apply(Field("home.hero.title", "el", "A"), Field("boat.hero.title", "el", "X"));
        _invalidator.Calls.Clear();

        var result = await Apply(
            Field("home.hero.title", "el", "B"),
            Field("home.hero.title", "el", "A"),
            Field("boat.hero.title", "el", "Y"));

        Assert.NotNull(result.SaveId);
        Assert.Equal(["boat"], Assert.Single(_invalidator.Calls));
        Assert.Contains("fields=1 ", _audit.Entries[^1].Details);
        Assert.Equal(3, (await HistoryRows()).Count);
    }

    [Fact]
    public async Task Write_FieldCreatedInBatchAlwaysCounts()
    {
        var result = await Apply(Field("home.hero.title", "el", ""));

        Assert.NotNull(result.SaveId);
        Assert.Single(await HistoryRows());
    }

    [Fact]
    public async Task Write_HistoryRowsOfOneCallShareASaveIdAndEachCallGetsItsOwn()
    {
        var first = await Apply(Field("home.hero.title", "el", "a"), Field("boat.hero.title", "el", "b"));
        var second = await Apply(Field("home.hero.title", "el", "c"));

        Assert.NotNull(first.SaveId);
        Assert.NotNull(second.SaveId);
        Assert.NotEqual(first.SaveId, second.SaveId);
        var rows = await HistoryRows();
        Assert.Equal(2, rows.Count(r => r.SaveId == first.SaveId));
        Assert.Single(rows, r => r.SaveId == second.SaveId);
    }

    [Fact]
    public async Task Write_SaveId_IsNullWhenNothingWasWrittenOrRejected()
    {
        await Apply(Field("home.hero.title", "el", "a"));

        Assert.Null((await Apply(Field("home.hero.title", "el", "a"))).SaveId);
        Assert.Null((await Apply(Field("bad", "el", "a"))).SaveId);
        Assert.Null((await Apply()).SaveId);
    }

    [Fact]
    public async Task GetFieldHistory_ReturnsVersionsNewestFirst()
    {
        await Apply(Field("home.hero.title", "el", "v1"));
        _time.Advance(TimeSpan.FromMinutes(1));
        var second = await Apply([Field("home.hero.title", "el", "v2")], new ContentEditor("other@example.com", null));
        _time.Advance(TimeSpan.FromMinutes(1));
        await Apply(Field("home.hero.title", "en", "english"));
        await using var context = CreateContext();

        var versions = await CreateService(context).GetFieldHistoryAsync("home.hero.title", "el", null);

        Assert.Equal(["v2", "v1"], versions.Select(v => v.Value));
        Assert.Equal(("other@example.com", second.SaveId), (versions[0].ChangedBy, versions[0].SaveId));
        Assert.Equal(DateTimeKind.Utc, versions[0].CreatedAt.Kind);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetFieldHistory_ListItemFieldsAndStandaloneFieldsAreSeparate()
    {
        var id = Guid.NewGuid();
        await Apply(
            new AddListItem("home.highlights", id),
            Field("home.highlights.title", "el", "item", id),
            Field("home.highlights.title", "el", "standalone"));
        await using var context = CreateContext();
        var service = CreateService(context);

        Assert.Equal(["item"], (await service.GetFieldHistoryAsync("home.highlights.title", "el", id)).Select(v => v.Value));
        Assert.Equal(["standalone"], (await service.GetFieldHistoryAsync("home.highlights.title", "el", null)).Select(v => v.Value));
    }

    [Fact]
    public async Task GetFieldHistory_UnknownFieldIsEmptyAndBadInputThrows()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        Assert.Empty(await service.GetFieldHistoryAsync("home.hero.title", "el", null));
        Assert.Empty(await service.GetFieldHistoryAsync("home.hero.title", "el", Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetFieldHistoryAsync("bad", "el", null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetFieldHistoryAsync("home.hero.title", "fr", null));
    }

    [Fact]
    public async Task GetSave_ReturnsWrittenAndPreviousValues()
    {
        var id = Guid.NewGuid();
        var first = await Apply(
            new AddListItem("home.highlights", id),
            Field("home.hero.title", "el", "t1"),
            Field("home.highlights.title", "el", "item1", id));
        _time.Advance(TimeSpan.FromMinutes(1));
        var second = await Apply(
            Field("home.hero.title", "el", "t2"),
            Field("home.highlights.title", "el", "item2", id),
            Field("home.hero.subtitle", "el", "s1"));
        await using var context = CreateContext();
        var service = CreateService(context);

        var firstDetails = await service.GetSaveAsync(first.SaveId!.Value);
        var secondDetails = await service.GetSaveAsync(second.SaveId!.Value);

        Assert.NotNull(firstDetails);
        Assert.Equal((Email, 2), (firstDetails.ChangedBy, firstDetails.Fields.Count));
        Assert.All(firstDetails.Fields, f => Assert.Equal("", f.PreviousValue));
        Assert.NotNull(secondDetails);
        Assert.Equal(DateTimeKind.Utc, secondDetails.CreatedAt.Kind);
        var byKey = secondDetails.Fields.ToDictionary(f => (f.Key, f.ListItemId));
        Assert.Equal(3, byKey.Count);
        Assert.Equal(("t2", "t1"), (byKey[("home.hero.title", null)].Value, byKey[("home.hero.title", null)].PreviousValue));
        Assert.Equal(("item2", "item1"), (byKey[("home.highlights.title", id)].Value, byKey[("home.highlights.title", id)].PreviousValue));
        Assert.Equal(("s1", ""), (byKey[("home.hero.subtitle", null)].Value, byKey[("home.hero.subtitle", null)].PreviousValue));
        Assert.Equal(ContentType.Text, byKey[("home.hero.title", null)].Type);
        Assert.Equal("el", byKey[("home.hero.title", null)].Locale);
    }

    [Fact]
    public async Task GetSave_CurrentValue_IsTheFieldsPresentValue()
    {
        var first = await Apply(Field("home.hero.title", "el", "t1"), Field("home.hero.subtitle", "el", "s1"));
        _time.Advance(TimeSpan.FromMinutes(1));
        await Apply(Field("home.hero.title", "el", "t2"));
        await using var context = CreateContext();
        var service = CreateService(context);

        var details = await service.GetSaveAsync(first.SaveId!.Value);

        var byKey = details!.Fields.ToDictionary(f => f.Key);
        Assert.Equal(("t1", "t2"), (byKey["home.hero.title"].Value, byKey["home.hero.title"].CurrentValue));
        Assert.Equal(("s1", "s1"), (byKey["home.hero.subtitle"].Value, byKey["home.hero.subtitle"].CurrentValue));
    }

    [Fact]
    public async Task GetSave_UnknownIdIsNull_AndDeletedListItemFieldsAreAbsent()
    {
        var id = Guid.NewGuid();
        var save = await Apply(
            new AddListItem("home.highlights", id),
            Field("home.highlights.title", "el", "item", id),
            Field("home.hero.title", "el", "t"));
        await Apply(new DeleteListItem("home.highlights", id));
        await using var context = CreateContext();
        var service = CreateService(context);

        Assert.Null(await service.GetSaveAsync(Guid.NewGuid()));
        var details = await service.GetSaveAsync(save.SaveId!.Value);
        Assert.Equal(["home.hero.title"], details!.Fields.Select(f => f.Key));
    }

    [Fact]
    public async Task Image_InEnglish_IsRejected()
    {
        var id = await SeedMediaAsync();

        var result = await Apply(Image("home.hero.image", "en", id.ToString()));

        Assert.Equal(["Images are stored in Greek only: 'home.hero.image'."], result.Errors);
        Assert.Empty(await HistoryRows());
    }

    [Fact]
    public async Task Image_UnknownId_IsRejected()
    {
        var unknown = Guid.NewGuid();

        var result = await Apply(
            Image("home.hero.image", "el", unknown.ToString()), Field("home.hero.image-alt", "el", "Alt"));

        Assert.Equal([$"Image '{unknown}' for 'home.hero.image' does not exist."], result.Errors);
        Assert.Empty(await HistoryRows());
    }

    [Fact]
    public async Task Image_NotAGuid_IsRejected()
    {
        var result = await Apply(Image("home.hero.image", "el", "not-a-guid"), Field("home.hero.image-alt", "el", "Alt"));

        Assert.Equal(["'home.hero.image': image value must be a media id."], result.Errors);
    }

    [Fact]
    public async Task Image_WithoutAlt_IsRejected()
    {
        var id = await SeedMediaAsync();

        var result = await Apply(Image("home.hero.image", "el", id.ToString()));

        Assert.Equal(["'home.hero.image': Greek alt text is required."], result.Errors);
        Assert.Empty(await HistoryRows());
    }

    [Fact]
    public async Task Image_WithOnlyEnglishAlt_IsRejected()
    {
        var id = await SeedMediaAsync();

        var result = await Apply(Image("home.hero.image", "el", id.ToString()), Field("home.hero.image-alt", "en", "Alt"));

        Assert.Equal(["'home.hero.image': Greek alt text is required."], result.Errors);
    }

    [Fact]
    public async Task ImageAndAlt_InTheSameBatch_AreAccepted()
    {
        var id = await SeedMediaAsync();

        var result = await Apply(Image("home.hero.image", "el", id.ToString()), Field("home.hero.image-alt", "el", "Alt"));

        Assert.True(result.Succeeded);
        var page = await Read("home", "el");
        Assert.Equal(new ContentValue(id.ToString(), ContentType.Image, false), page.GetField("home.hero.image"));
    }

    [Fact]
    public async Task Image_WithAltAlreadyStored_IsAccepted()
    {
        var id = await SeedMediaAsync();
        await Apply(Field("home.hero.image-alt", "el", "Alt"));

        var result = await Apply(Image("home.hero.image", "el", id.ToString()));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ImageIdIsStoredNormalized()
    {
        var id = await SeedMediaAsync();

        var result = await Apply(
            Image("home.hero.image", "el", $" {id.ToString("N").ToUpperInvariant()} "), Field("home.hero.image-alt", "el", "Alt"));

        Assert.True(result.Succeeded);
        Assert.Equal(id.ToString(), (await Read("home", "el")).GetField("home.hero.image")?.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ClearingAlt_WhileTheImageStays_IsRejected(string alt)
    {
        var id = await SeedMediaAsync();
        await Apply(Image("home.hero.image", "el", id.ToString()), Field("home.hero.image-alt", "el", "Alt"));

        var result = await Apply(Field("home.hero.image-alt", "el", alt));

        Assert.Equal(["'home.hero.image': Greek alt text is required."], result.Errors);
        Assert.Equal("Alt", (await Read("home", "el")).GetField("home.hero.image-alt")?.Value);
    }

    [Fact]
    public async Task WhitespaceAlt_WithANewImage_IsRejected()
    {
        var id = await SeedMediaAsync();

        var result = await Apply(Image("home.hero.image", "el", id.ToString()), Field("home.hero.image-alt", "el", "   "));

        Assert.Equal(["'home.hero.image': Greek alt text is required."], result.Errors);
    }

    [Fact]
    public async Task ClearingTheImage_IsAcceptedWithoutAlt()
    {
        var id = await SeedMediaAsync();
        await Apply(Image("home.hero.image", "el", id.ToString()), Field("home.hero.image-alt", "el", "Alt"));

        var clearBoth = await Apply(Image("home.hero.image", "el", ""), Field("home.hero.image-alt", "el", ""));
        var neverHadAlt = await Apply(Image("home.other.image", "el", ""));

        Assert.True(clearBoth.Succeeded);
        Assert.True(neverHadAlt.Succeeded);
        Assert.Equal("", (await Read("home", "el")).GetField("home.hero.image")?.Value);
    }

    [Fact]
    public async Task ListItemImage_NeedsAltOnTheSameItem()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var id = await SeedMediaAsync();

        var accepted = await Apply(
            new AddListItem("home.gallery", first),
            Image("home.gallery.image", "el", id.ToString(), first),
            Field("home.gallery.image-alt", "el", "First", first));
        var rejected = await Apply(
            new AddListItem("home.gallery", second),
            Image("home.gallery.image", "el", id.ToString(), second));

        Assert.True(accepted.Succeeded);
        Assert.Equal(["'home.gallery.image': Greek alt text is required."], rejected.Errors);
    }

    [Fact]
    public async Task ListItemImage_AltStoredOnAnotherItem_DoesNotCount()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var id = await SeedMediaAsync();
        await Apply(
            new AddListItem("home.gallery", first),
            new AddListItem("home.gallery", second),
            Field("home.gallery.image-alt", "el", "First", first));

        var result = await Apply(Image("home.gallery.image", "el", id.ToString(), second));

        Assert.Equal(["'home.gallery.image': Greek alt text is required."], result.Errors);
    }

    [Fact]
    public async Task ImageOfAListItemDeletedInTheSameBatch_IsNotChecked()
    {
        var item = Guid.NewGuid();
        await Apply(new AddListItem("home.gallery", item));

        var result = await Apply(
            Image("home.gallery.image", "el", Guid.NewGuid().ToString(), item),
            new DeleteListItem("home.gallery", item));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task BatchWithTwoImages_ChecksExistenceWithOneQuery()
    {
        var first = await SeedMediaAsync();
        var second = await SeedMediaAsync();
        _existCalls = 0;

        var result = await Apply(
            Image("home.hero.image", "el", first.ToString()),
            Field("home.hero.image-alt", "el", "A"),
            Image("home.boat.image", "el", second.ToString()),
            Field("home.boat.image-alt", "el", "B"));
        var withoutImages = await Apply(Field("home.hero.title", "el", "t"));

        Assert.True(result.Succeeded);
        Assert.True(withoutImages.Succeeded);
        Assert.Equal(1, _existCalls);
    }

    [Fact]
    public async Task Read_ResolvesAllImagesWithOneMediaQuery()
    {
        var first = await SeedMediaAsync();
        var second = await SeedMediaAsync();
        var item = Guid.NewGuid();
        await Apply(
            Image("home.hero.image", "el", first.ToString()),
            Field("home.hero.image-alt", "el", "A"),
            Image("shared.footer.logo", "el", first.ToString()),
            Field("shared.footer.logo-alt", "el", "L"),
            new AddListItem("home.cards", item),
            Image("home.cards.image", "el", second.ToString(), item),
            Field("home.cards.image-alt", "el", "B", item));
        _getByIdsCalls = 0;

        var page = await Read("home", "en");

        Assert.Equal(1, _getByIdsCalls);
        var hero = page.GetImage(first)!;
        Assert.Equal(first, hero.Id);
        Assert.Equal((1000, 800), (hero.Width, hero.Height));
        Assert.Equal([480, 960], hero.Sources.Select(s => s.Width));
        Assert.Matches("^/media/photo-[0-9a-f]{16}-480[.]webp$", hero.Sources[0].Url);
        Assert.Matches("^/media/photo-[0-9a-f]{16}-960[.]webp$", hero.Sources[1].Url);
        Assert.Equal(640, hero.HeightAt(800));
        Assert.NotNull(page.GetImage(second));
    }

    [Fact]
    public async Task Read_WithoutImages_DoesNotQueryMedia()
    {
        await Apply(Field("home.hero.title", "el", "t"));
        _getByIdsCalls = 0;

        var page = await Read("home", "el");

        Assert.Equal(0, _getByIdsCalls);
        Assert.Null(page.GetImage(Guid.NewGuid()));
    }

    [Fact]
    public async Task Read_IgnoresUnknownImageIds()
    {
        var id = await SeedMediaAsync();
        await Apply(Image("home.hero.image", "el", id.ToString()), Field("home.hero.image-alt", "el", "A"));
        await using (var context = CreateContext())
        {
            context.Set<MediaAsset>().Remove(await context.Set<MediaAsset>().SingleAsync(a => a.Id == id));
            await context.SaveChangesAsync();
        }

        var page = await Read("home", "el");

        Assert.Null(page.GetImage(id));
        Assert.Equal(id.ToString(), page.GetField("home.hero.image")!.Value);
    }

    private static SetField Image(string key, string locale, string value, Guid? listItemId = null) =>
        new(key, locale, ContentType.Image, value, listItemId);

    private int _existCalls;

    private int _getByIdsCalls;

    private async Task<Guid> SeedMediaAsync()
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        var asset = MediaAsset.Create("photo.jpg", hash, "photo", [480, 960], 1000, 800, ".jpg");
        await using var context = CreateContext();
        context.Set<MediaAsset>().Add(asset);
        await context.SaveChangesAsync();
        return asset.Id;
    }

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
            new CountingMediaAssetRepository(new MediaAssetRepository(context), () => _existCalls++, () => _getByIdsCalls++),
            new UnitOfWork(context),
            new RichTextSanitizer(),
            _invalidator,
            _audit);

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options, _time);

    private sealed class CountingMediaAssetRepository(IMediaAssetRepository inner, Action onExist, Action? onGetByIds = null) : IMediaAssetRepository
    {
        public Task<MediaAsset?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<MediaAsset>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        {
            onGetByIds?.Invoke();
            return inner.GetByIdsAsync(ids, ct);
        }

        public Task<MediaAsset?> GetByContentHashAsync(string contentHash, CancellationToken ct = default) =>
            inner.GetByContentHashAsync(contentHash, ct);

        public Task<IReadOnlyList<MediaAsset>> ListNewestFirstAsync(int skip, int take, CancellationToken ct = default) =>
            inner.ListNewestFirstAsync(skip, take, ct);

        public Task<IReadOnlySet<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        {
            onExist();
            return inner.ExistAsync(ids, ct);
        }

        public void Add(MediaAsset asset) => inner.Add(asset);
    }

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
