using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

internal sealed class ContentBatch(
    IContentItemRepository items,
    IContentListItemRepository listItems,
    IContentSectionRepository sections,
    IContentHistoryRepository history,
    IMediaAssetRepository media,
    IRichTextSanitizer sanitizer,
    string editorEmail)
{
    private const string AltSuffix = "-alt";

    private readonly List<string> _errors = [];
    private readonly HashSet<string> _touchedPages = [];
    private readonly Dictionary<FieldRef, FieldState> _fields = [];
    private readonly List<FieldState> _fieldOrder = [];
    private readonly Dictionary<Guid, ContentListItem> _knownListItems = [];
    private readonly List<ContentListItem> _newListItems = [];
    private readonly HashSet<Guid> _deletedIds = [];
    private readonly Dictionary<(string Page, string List), List<ContentListItem>> _lists = [];
    private readonly Dictionary<(string Page, string Section), ContentSection> _sections = [];
    private readonly List<ContentSection> _newSections = [];
    private readonly Dictionary<(string Page, string Section), bool> _sectionOriginalStates = [];

    public IReadOnlyList<string> Errors => _errors;

    public IReadOnlyCollection<string> TouchedPages => _touchedPages;

    public Guid SaveId { get; } = Guid.CreateVersion7();

    public int FieldsWritten { get; private set; }

    public int ListItemsAdded { get; private set; }

    public int ListItemsDeleted { get; private set; }

    public int ListsReordered { get; private set; }

    public int ListItemsActivated { get; private set; }

    public int SectionsChanged { get; private set; }

    public IReadOnlyList<(string PageKey, string SectionKey, bool IsActive)> SectionChanges =>
        [.. _sectionOriginalStates
            .Where(s => _sections[s.Key].IsActive != s.Value)
            .Select(s => (s.Key.Page, s.Key.Section, _sections[s.Key].IsActive))];

    public async Task ApplyAsync(ContentChange change, CancellationToken ct)
    {
        switch (change)
        {
            case SetField c:
                await SetFieldAsync(c, ct);
                break;
            case AddListItem c:
                await AddListItemAsync(c, ct);
                break;
            case DeleteListItem c:
                await DeleteListItemAsync(c, ct);
                break;
            case ReorderList c:
                await ReorderListAsync(c, ct);
                break;
            case SetListItemActive c:
                await SetListItemActiveAsync(c, ct);
                break;
            case SetSectionActive c:
                await SetSectionActiveAsync(c, ct);
                break;
            default:
                _errors.Add($"Unsupported change type '{change?.GetType().Name ?? "null"}'.");
                break;
        }
    }

    public async Task ValidateImagesAsync(CancellationToken ct)
    {
        var filled = _fieldOrder
            .Where(s => s.Item.Type == ContentType.Image
                && s.Item.Locale == ContentLocales.Default
                && s.Current.Length > 0
                && !BelongsToDeletedItem(s.Item))
            .ToList();

        if (filled.Count > 0)
        {
            var ids = filled.Select(s => Guid.TryParse(s.Current, out var id) ? id : Guid.Empty).Distinct().ToList();
            var existing = await media.ExistAsync(ids, ct);
            foreach (var state in filled)
            {
                if (!Guid.TryParse(state.Current, out var id) || !existing.Contains(id))
                {
                    _errors.Add($"Image '{state.Current}' for '{KeyOf(state.Item)}' does not exist.");
                }
            }
        }

        var toCheck = new List<FieldRef>();
        var seen = new HashSet<FieldRef>();
        foreach (var state in filled)
        {
            var reference = RefOf(state.Item);
            if (seen.Add(reference))
            {
                toCheck.Add(reference);
            }
        }

        var clearedAlts = _fieldOrder
            .Where(s => s.Item.Type == ContentType.Text
                && s.Item.Locale == ContentLocales.Default
                && s.Item.FieldKey.Length > AltSuffix.Length
                && s.Item.FieldKey.EndsWith(AltSuffix, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(s.Current)
                && !BelongsToDeletedItem(s.Item))
            .ToList();
        foreach (var state in clearedAlts)
        {
            var item = state.Item;
            var imageRef = new FieldRef(
                item.PageKey, item.SectionKey, item.FieldKey[..^AltSuffix.Length], item.Locale, item.ListItemId);
            if (!seen.Contains(imageRef) && !string.IsNullOrEmpty(await CurrentValueAsync(imageRef, ct)))
            {
                seen.Add(imageRef);
                toCheck.Add(imageRef);
            }
        }

        foreach (var imageRef in toCheck)
        {
            var altRef = imageRef with { Field = imageRef.Field + AltSuffix };
            if (string.IsNullOrWhiteSpace(await CurrentValueAsync(altRef, ct)))
            {
                _errors.Add($"'{imageRef.Page}.{imageRef.Section}.{imageRef.Field}': Greek alt text is required.");
            }
        }
    }

    public void Finish()
    {
        foreach (var state in _fieldOrder.Where(s => s.IsChanged))
        {
            if (state.Item.Value != state.Current)
            {
                state.Item.SetValue(state.Current, editorEmail);
            }

            FieldsWritten++;
            _touchedPages.Add(state.Item.PageKey);
        }
    }

    public void Stage()
    {
        foreach (var listItem in _newListItems.Where(l => !_deletedIds.Contains(l.Id)))
        {
            listItems.Add(listItem);
        }

        foreach (var state in _fieldOrder.Where(s => s.IsChanged && !BelongsToDeletedItem(s.Item)))
        {
            if (state.IsNew)
            {
                items.Add(state.Item);
            }

            history.Add(new ContentHistory
            {
                ContentItemId = state.Item.Id,
                SaveId = SaveId,
                Value = state.Current,
                ChangedBy = editorEmail
            });
        }

        foreach (var section in _newSections)
        {
            sections.Add(section);
        }
    }

    private async Task<string> CurrentValueAsync(FieldRef reference, CancellationToken ct)
    {
        if (_fields.TryGetValue(reference, out var state))
        {
            return state.Current;
        }

        var stored = reference.ListItemId is { } id
            ? await items.FindListFieldAsync(id, reference.Field, reference.Locale, ct)
            : await items.FindStandaloneAsync(reference.Page, reference.Section, reference.Field, reference.Locale, ct);
        return stored?.Value ?? "";
    }

    private static FieldRef RefOf(ContentItem item) =>
        new(item.PageKey, item.SectionKey, item.FieldKey, item.Locale, item.ListItemId);

    private static string KeyOf(ContentItem item) => $"{item.PageKey}.{item.SectionKey}.{item.FieldKey}";

    private bool BelongsToDeletedItem(ContentItem item) =>
        item.ListItemId is { } id && _deletedIds.Contains(id);

    private async Task SetFieldAsync(SetField change, CancellationToken ct)
    {
        if (!ContentKeys.TryParseField(change.Key, out var page, out var section, out var field))
        {
            _errors.Add($"Invalid field key '{change.Key}'.");
            return;
        }

        if (!ContentLocales.IsSupported(change.Locale))
        {
            _errors.Add($"Unsupported locale '{change.Locale}' for '{change.Key}'.");
            return;
        }

        if (!Enum.IsDefined(change.Type))
        {
            _errors.Add($"Unknown content type for '{change.Key}'.");
            return;
        }

        if (change.Value is null)
        {
            _errors.Add($"Missing value for '{change.Key}'.");
            return;
        }

        if (change.ListItemId is { } listItemId)
        {
            var listItem = await FindListItemAsync(listItemId, ct);
            if (listItem is null)
            {
                _errors.Add($"List item {listItemId} does not exist.");
                return;
            }

            if (listItem.PageKey != page || listItem.ListKey != section)
            {
                _errors.Add($"List item {listItemId} does not belong to '{page}.{section}'.");
                return;
            }
        }

        var value = change.Value;
        if (change.Type == ContentType.RichText)
        {
            value = sanitizer.Sanitize(value);
        }
        else if (change.Type == ContentType.Link && value.Length > 0 && !ContentLink.TryParse(value, out _))
        {
            _errors.Add(
                $"Invalid link for '{change.Key}' ({change.Locale}): URL must be https:, mailto: or tel: and label is required.");
            return;
        }
        else if (change.Type == ContentType.Image)
        {
            if (change.Locale != ContentLocales.Default)
            {
                _errors.Add($"Images are stored in Greek only: '{change.Key}'.");
                return;
            }

            if (value.Length > 0)
            {
                if (!Guid.TryParse(value, out var mediaId))
                {
                    _errors.Add($"'{change.Key}': image value must be a media id.");
                    return;
                }

                value = mediaId.ToString();
            }
        }

        var reference = new FieldRef(page, section, field, change.Locale, change.ListItemId);
        if (!_fields.TryGetValue(reference, out var state))
        {
            var existing = change.ListItemId is { } id
                ? await items.FindListFieldAsync(id, field, change.Locale, ct)
                : await items.FindStandaloneAsync(page, section, field, change.Locale, ct);
            if (existing is null)
            {
                var created = ContentItem.Create(
                    page, section, field, change.Locale, change.ListItemId, change.Type, value, editorEmail);
                state = new FieldState(created, null, value);
            }
            else
            {
                state = new FieldState(existing, existing.Value, existing.Value);
            }

            _fields[reference] = state;
            _fieldOrder.Add(state);
        }

        if (state.Item.Type != change.Type)
        {
            _errors.Add($"'{change.Key}' ({change.Locale}) is {state.Item.Type}, not {change.Type}.");
            return;
        }

        state.Current = value;
    }

    private async Task AddListItemAsync(AddListItem change, CancellationToken ct)
    {
        if (!TryParseList(change.ListKey, out var page, out var listKey))
        {
            return;
        }

        if (change.Id == Guid.Empty)
        {
            _errors.Add($"List item Id for '{change.ListKey}' is required.");
            return;
        }

        if (_deletedIds.Contains(change.Id) || await FindListItemAsync(change.Id, ct) is not null)
        {
            _errors.Add($"List item {change.Id} already exists.");
            return;
        }

        var list = await GetListAsync(page, listKey, ct);
        var sortOrder = list.Count == 0 ? 0 : list.Max(l => l.SortOrder) + 1;
        var created = ContentListItem.Create(page, listKey, sortOrder, change.Id);
        _newListItems.Add(created);
        _knownListItems[created.Id] = created;
        list.Add(created);
        ListItemsAdded++;
        _touchedPages.Add(page);
    }

    private async Task DeleteListItemAsync(DeleteListItem change, CancellationToken ct)
    {
        var item = await FindOwnedListItemAsync(change.ListKey, change.Id, ct);
        if (item is null)
        {
            return;
        }

        if (!_newListItems.Contains(item))
        {
            listItems.Remove(item);
        }

        _deletedIds.Add(item.Id);
        _knownListItems.Remove(item.Id);
        _lists.GetValueOrDefault((item.PageKey, item.ListKey))?.Remove(item);
        ListItemsDeleted++;
        _touchedPages.Add(item.PageKey);
    }

    private async Task ReorderListAsync(ReorderList change, CancellationToken ct)
    {
        if (!TryParseList(change.ListKey, out var page, out var listKey))
        {
            return;
        }

        var list = await GetListAsync(page, listKey, ct);
        var ids = change.OrderedIds ?? [];
        if (ids.Count != list.Count
            || ids.Distinct().Count() != ids.Count
            || !list.Select(l => l.Id).ToHashSet().SetEquals(ids))
        {
            _errors.Add($"Reorder of '{change.ListKey}' must list each current item exactly once.");
            return;
        }

        var byId = list.ToDictionary(l => l.Id);
        var moved = false;
        for (var index = 0; index < ids.Count; index++)
        {
            var item = byId[ids[index]];
            if (item.SortOrder != index)
            {
                item.MoveTo(index);
                moved = true;
            }
        }

        list.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        if (moved)
        {
            ListsReordered++;
            _touchedPages.Add(page);
        }
    }

    private async Task SetListItemActiveAsync(SetListItemActive change, CancellationToken ct)
    {
        var item = await FindOwnedListItemAsync(change.ListKey, change.Id, ct);
        if (item is null || item.IsActive == change.IsActive)
        {
            return;
        }

        item.SetActive(change.IsActive);
        ListItemsActivated++;
        _touchedPages.Add(item.PageKey);
    }

    private async Task SetSectionActiveAsync(SetSectionActive change, CancellationToken ct)
    {
        if (!ContentKeys.TryParseSection(change.SectionKey, out var page, out var sectionKey))
        {
            _errors.Add($"Invalid section key '{change.SectionKey}'.");
            return;
        }

        if (!_sections.TryGetValue((page, sectionKey), out var section))
        {
            section = await sections.FindAsync(page, sectionKey, ct);
            if (section is not null)
            {
                _sections[(page, sectionKey)] = section;
            }
        }

        if (section is null)
        {
            if (change.IsActive)
            {
                return;
            }

            _sectionOriginalStates.TryAdd((page, sectionKey), true);
            section = ContentSection.Create(page, sectionKey, isActive: false);
            _sections[(page, sectionKey)] = section;
            _newSections.Add(section);
        }
        else if (section.IsActive != change.IsActive)
        {
            _sectionOriginalStates.TryAdd((page, sectionKey), section.IsActive);
            section.SetActive(change.IsActive);
        }
        else
        {
            return;
        }

        SectionsChanged++;
        _touchedPages.Add(page);
    }

    private bool TryParseList(string listKey, out string page, out string list)
    {
        if (ContentKeys.TryParseList(listKey, out var p, out var l))
        {
            (page, list) = (p, l);
            return true;
        }

        (page, list) = ("", "");
        _errors.Add($"Invalid list key '{listKey}'.");
        return false;
    }

    private async Task<ContentListItem?> FindOwnedListItemAsync(string listKey, Guid id, CancellationToken ct)
    {
        if (!TryParseList(listKey, out var page, out var list))
        {
            return null;
        }

        var item = await FindListItemAsync(id, ct);
        if (item is null)
        {
            _errors.Add($"List item {id} does not exist.");
            return null;
        }

        if (item.PageKey != page || item.ListKey != list)
        {
            _errors.Add($"List item {id} does not belong to '{listKey}'.");
            return null;
        }

        return item;
    }

    private async Task<ContentListItem?> FindListItemAsync(Guid id, CancellationToken ct)
    {
        if (_deletedIds.Contains(id))
        {
            return null;
        }

        if (_knownListItems.TryGetValue(id, out var known))
        {
            return known;
        }

        var found = await listItems.FindAsync(id, ct);
        if (found is not null)
        {
            _knownListItems[id] = found;
        }

        return found;
    }

    private async Task<List<ContentListItem>> GetListAsync(string page, string listKey, CancellationToken ct)
    {
        if (_lists.TryGetValue((page, listKey), out var cached))
        {
            return cached;
        }

        var stored = await listItems.GetListAsync(page, listKey, ct);
        var list = stored
            .Where(l => !_deletedIds.Contains(l.Id))
            .Concat(_newListItems.Where(l => l.PageKey == page && l.ListKey == listKey && !_deletedIds.Contains(l.Id)))
            .OrderBy(l => l.SortOrder)
            .ToList();
        foreach (var item in list)
        {
            _knownListItems[item.Id] = item;
        }

        _lists[(page, listKey)] = list;
        return list;
    }

    private sealed class FieldState(ContentItem item, string? original, string current)
    {
        public ContentItem Item { get; } = item;

        public string Current { get; set; } = current;

        public bool IsNew => original is null;

        public bool IsChanged => IsNew || Current != original;
    }

    private readonly record struct FieldRef(string Page, string Section, string Field, string Locale, Guid? ListItemId);
}
