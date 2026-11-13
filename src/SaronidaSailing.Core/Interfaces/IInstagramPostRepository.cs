using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public interface IInstagramPostRepository
{
    Task<IReadOnlyList<InstagramPost>> ListNewestFirstAsync(int take, CancellationToken ct = default);

    Task<IReadOnlyList<InstagramPost>> ListAllAsync(CancellationToken ct = default);

    void Add(InstagramPost post);

    Task<int> CountAsync(CancellationToken ct = default);

    void Remove(InstagramPost post);
}
