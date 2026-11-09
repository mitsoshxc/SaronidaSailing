using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public abstract record ContentChange;

public sealed record SetField(string Key, string Locale, ContentType Type, string Value, Guid? ListItemId = null)
    : ContentChange;

public sealed record AddListItem(string ListKey, Guid Id) : ContentChange;

public sealed record DeleteListItem(string ListKey, Guid Id) : ContentChange;

public sealed record ReorderList(string ListKey, IReadOnlyList<Guid> OrderedIds) : ContentChange;

public sealed record SetListItemActive(string ListKey, Guid Id, bool IsActive) : ContentChange;

public sealed record SetSectionActive(string SectionKey, bool IsActive) : ContentChange;
