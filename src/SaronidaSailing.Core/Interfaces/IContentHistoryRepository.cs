using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public record HistoryEntry(ContentHistory History, ContentItem Item);

public interface IContentHistoryRepository
{
    Task<IReadOnlyList<ContentHistory>> GetForItemAsync(Guid contentItemId, CancellationToken ct = default);

    Task<IReadOnlyList<ContentHistory>> GetForFieldAsync(
        string pageKey, string sectionKey, string fieldKey, string locale, Guid? listItemId, CancellationToken ct = default);

    Task<IReadOnlyList<HistoryEntry>> GetForSaveAsync(Guid saveId, CancellationToken ct = default);

    Task<IReadOnlyList<ContentHistory>> GetForItemsAsync(
        IReadOnlyCollection<Guid> contentItemIds, CancellationToken ct = default);

    void Add(ContentHistory entry);
}
