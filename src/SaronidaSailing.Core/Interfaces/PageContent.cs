using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public record ContentValue(string Value, ContentType Type, bool IsFallback);

public record ContentListEntry(
    Guid Id, int SortOrder, bool IsActive, IReadOnlyDictionary<string, ContentValue> Fields);

public sealed class PageContent(
    string pageKey,
    string locale,
    IReadOnlyDictionary<string, ContentValue> fields,
    IReadOnlyDictionary<string, IReadOnlyList<ContentListEntry>> lists,
    IReadOnlyDictionary<string, bool> sections)
{
    public string PageKey { get; } = pageKey;

    public string Locale { get; } = locale;

    public ContentValue? GetField(string key) => fields.GetValueOrDefault(key);

    public IReadOnlyList<ContentListEntry> GetList(string listKey) => lists.GetValueOrDefault(listKey) ?? [];

    public bool IsSectionActive(string sectionKey) => sections.GetValueOrDefault(sectionKey, true);
}
