using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public interface IContentListItemRepository
{
    Task<IReadOnlyList<ContentListItem>> GetForPagesAsync(
        IReadOnlyCollection<string> pageKeys, CancellationToken ct = default);

    Task<IReadOnlyList<ContentListItem>> GetListAsync(string pageKey, string listKey, CancellationToken ct = default);

    Task<ContentListItem?> FindAsync(Guid id, CancellationToken ct = default);

    void Add(ContentListItem item);

    void Remove(ContentListItem item);
}
