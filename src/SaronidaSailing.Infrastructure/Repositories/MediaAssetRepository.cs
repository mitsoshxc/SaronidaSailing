using Microsoft.EntityFrameworkCore;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Repositories;

public sealed class MediaAssetRepository(AppDbContext db) : IMediaAssetRepository
{
    public Task<MediaAsset?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Set<MediaAsset>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<MediaAsset>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await db.Set<MediaAsset>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);

    public Task<MediaAsset?> GetByContentHashAsync(string contentHash, CancellationToken ct = default) =>
        db.Set<MediaAsset>().FirstOrDefaultAsync(x => x.ContentHash == contentHash, ct);

    public async Task<IReadOnlyList<MediaAsset>> ListNewestFirstAsync(int skip, int take, CancellationToken ct = default) =>
        await db.Set<MediaAsset>()
            .AsNoTracking()
            .Where(x => x.Source == MediaSource.Upload)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlySet<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        (await db.Set<MediaAsset>()
            .AsNoTracking()
            .Where(x => x.Source == MediaSource.Upload && ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct))
        .ToHashSet();

    public void Add(MediaAsset asset) => db.Set<MediaAsset>().Add(asset);
}
