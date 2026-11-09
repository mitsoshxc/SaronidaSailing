using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public interface IContentItemRepository
{
    Task<IReadOnlyList<ContentItem>> GetForPagesAsync(
        IReadOnlyCollection<string> pageKeys, IReadOnlyCollection<string> locales, CancellationToken ct = default);

    Task<ContentItem?> FindStandaloneAsync(
        string pageKey, string sectionKey, string fieldKey, string locale, CancellationToken ct = default);

    Task<ContentItem?> FindListFieldAsync(
        Guid listItemId, string fieldKey, string locale, CancellationToken ct = default);

    void Add(ContentItem item);
}
