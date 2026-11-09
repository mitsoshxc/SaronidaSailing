using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public interface IContentHistoryRepository
{
    Task<IReadOnlyList<ContentHistory>> GetForItemAsync(Guid contentItemId, CancellationToken ct = default);

    void Add(ContentHistory entry);
}
