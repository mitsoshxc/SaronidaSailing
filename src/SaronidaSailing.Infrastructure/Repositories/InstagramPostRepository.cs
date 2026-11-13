using Microsoft.EntityFrameworkCore;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Repositories;

public sealed class InstagramPostRepository(AppDbContext db) : IInstagramPostRepository
{
    public async Task<IReadOnlyList<InstagramPost>> ListNewestFirstAsync(int take, CancellationToken ct = default) =>
        await db.Set<InstagramPost>()
            .AsNoTracking()
            .OrderByDescending(x => x.PostedAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InstagramPost>> ListAllAsync(CancellationToken ct = default) =>
        await db.Set<InstagramPost>().ToListAsync(ct);

    public void Add(InstagramPost post) => db.Set<InstagramPost>().Add(post);

    public Task<int> CountAsync(CancellationToken ct = default) => db.Set<InstagramPost>().CountAsync(ct);

    public void Remove(InstagramPost post) => db.Set<InstagramPost>().Remove(post);
}
