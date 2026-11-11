using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public interface IMediaAssetRepository
{
    Task<MediaAsset?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<MediaAsset>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task<MediaAsset?> GetByContentHashAsync(string contentHash, CancellationToken ct = default);

    Task<IReadOnlyList<MediaAsset>> ListNewestFirstAsync(int skip, int take, CancellationToken ct = default);

    Task<IReadOnlySet<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    void Add(MediaAsset asset);
}
