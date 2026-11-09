using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class ContentService(
    IContentItemRepository items,
    IContentListItemRepository listItems,
    IContentSectionRepository sections,
    IContentHistoryRepository history,
    IUnitOfWork unitOfWork,
    IRichTextSanitizer sanitizer,
    IPageCacheInvalidator cacheInvalidator,
    IAuditLog auditLog) : IContentService
{
    public async Task<PageContent> GetPageContentAsync(string pageKey, string locale, CancellationToken ct = default)
    {
        if (!ContentKeys.IsValidSegment(pageKey))
        {
            throw new ArgumentException($"Invalid page key '{pageKey}'.", nameof(pageKey));
        }

        if (!ContentLocales.IsSupported(locale))
        {
            throw new ArgumentException($"Unsupported locale '{locale}'.", nameof(locale));
        }

        string[] pageKeys = [.. new[] { pageKey, ContentKeys.Shared }.Distinct()];
        string[] locales = [.. new[] { locale, ContentLocales.Default }.Distinct()];

        var itemRows = await items.GetForPagesAsync(pageKeys, locales, ct);
        var listRows = await listItems.GetForPagesAsync(pageKeys, ct);
        var sectionRows = await sections.GetForPagesAsync(pageKeys, ct);

        var fields = new Dictionary<string, ContentValue>();
        foreach (var group in itemRows.Where(i => i.ListItemId is null).GroupBy(i => (i.PageKey, i.SectionKey, i.FieldKey)))
        {
            fields[$"{group.Key.PageKey}.{group.Key.SectionKey}.{group.Key.FieldKey}"] = Resolve(group, locale);
        }

        var fieldsByListItem = itemRows
            .Where(i => i.ListItemId is not null)
            .GroupBy(i => i.ListItemId!.Value)
            .ToDictionary(g => g.Key, g => g.GroupBy(i => i.FieldKey).ToList());

        var lists = listRows
            .GroupBy(l => $"{l.PageKey}.{l.ListKey}")
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ContentListEntry>)[.. g.OrderBy(l => l.SortOrder).Select(l => new ContentListEntry(
                    l.Id,
                    l.SortOrder,
                    l.IsActive,
                    fieldsByListItem.TryGetValue(l.Id, out var byField)
                        ? byField.ToDictionary(f => f.Key, f => Resolve(f, locale))
                        : new Dictionary<string, ContentValue>()))]);

        var sectionStates = sectionRows.ToDictionary(s => $"{s.PageKey}.{s.SectionKey}", s => s.IsActive);

        return new PageContent(pageKey, locale, fields, lists, sectionStates);
    }

    public async Task<ContentSaveResult> ApplyChangesAsync(
        IReadOnlyList<ContentChange> changes, ContentEditor editor, CancellationToken ct = default)
    {
        if (changes.Count == 0)
        {
            return ContentSaveResult.Success;
        }

        if (string.IsNullOrWhiteSpace(editor.Email))
        {
            return Reject(editor, ["Editor email is required."]);
        }

        var batch = new ContentBatch(items, listItems, sections, history, sanitizer, editor.Email);
        foreach (var change in changes)
        {
            await batch.ApplyAsync(change, ct);
        }

        if (batch.Errors.Count > 0)
        {
            return Reject(editor, batch.Errors);
        }

        batch.Finish();
        if (batch.TouchedPages.Count == 0)
        {
            return ContentSaveResult.Success;
        }

        batch.Stage();
        await unitOfWork.SaveChangesAsync(ct);

        var pages = batch.TouchedPages.Order(StringComparer.Ordinal).ToArray();
        await cacheInvalidator.EvictPagesAsync(pages, ct);

        auditLog.Record(
            "content-saved",
            editor.Email,
            editor.IpAddress,
            $"pages={string.Join(',', pages)} fields={batch.FieldsWritten} list-added={batch.ListItemsAdded} "
            + $"list-deleted={batch.ListItemsDeleted} reordered={batch.ListsReordered} "
            + $"list-activated={batch.ListItemsActivated} sections={batch.SectionsChanged} save={batch.SaveId}");

        return ContentSaveResult.Saved(batch.SaveId);
    }

    public async Task<IReadOnlyList<FieldVersion>> GetFieldHistoryAsync(
        string key, string locale, Guid? listItemId, CancellationToken ct = default)
    {
        if (!ContentKeys.TryParseField(key, out var page, out var section, out var field))
        {
            throw new ArgumentException($"Invalid field key '{key}'.", nameof(key));
        }

        if (!ContentLocales.IsSupported(locale))
        {
            throw new ArgumentException($"Unsupported locale '{locale}'.", nameof(locale));
        }

        var rows = await history.GetForFieldAsync(page, section, field, locale, listItemId, ct);
        return [.. rows.Select(r => new FieldVersion(r.SaveId, r.Value, r.ChangedBy, r.CreatedAt))];
    }

    public async Task<ContentSaveDetails?> GetSaveAsync(Guid saveId, CancellationToken ct = default)
    {
        var entries = await history.GetForSaveAsync(saveId, ct);
        if (entries.Count == 0)
        {
            return null;
        }

        var itemIds = entries.Select(e => e.Item.Id).ToArray();
        var versionsByItem = (await history.GetForItemsAsync(itemIds, ct))
            .GroupBy(h => h.ContentItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var fields = new List<SavedField>(entries.Count);
        foreach (var (row, item) in entries)
        {
            var versions = versionsByItem[item.Id];
            var index = versions.FindIndex(v => v.Id == row.Id);
            var previous = index >= 0 && index + 1 < versions.Count ? versions[index + 1].Value : "";
            fields.Add(new SavedField(
                $"{item.PageKey}.{item.SectionKey}.{item.FieldKey}",
                item.Locale,
                item.Type,
                item.ListItemId,
                row.Value,
                previous,
                item.Value));
        }

        var first = entries[0].History;
        return new ContentSaveDetails(saveId, first.ChangedBy, entries.Min(e => e.History.CreatedAt), fields);
    }

    private ContentSaveResult Reject(ContentEditor editor, IReadOnlyList<string> errors)
    {
        unitOfWork.DiscardChanges();
        auditLog.Record(
            "content-save-rejected", editor.Email, editor.IpAddress, $"errors={errors.Count} first={errors[0]}");
        return new ContentSaveResult(false, [.. errors]);
    }

    private static ContentValue Resolve(IEnumerable<ContentItem> rows, string locale)
    {
        var all = rows.ToList();
        var requested = all.FirstOrDefault(r => r.Locale == locale);
        if (requested is not null && !string.IsNullOrWhiteSpace(requested.Value))
        {
            return new ContentValue(requested.Value, requested.Type, false);
        }

        if (locale != ContentLocales.Default && all.FirstOrDefault(r => r.Locale == ContentLocales.Default) is { } greek)
        {
            return new ContentValue(greek.Value, greek.Type, true);
        }

        return new ContentValue(requested!.Value, requested.Type, false);
    }
}
