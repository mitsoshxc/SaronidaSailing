using Microsoft.EntityFrameworkCore;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Repositories;

public sealed class ContentHistoryRepository(AppDbContext db) : IContentHistoryRepository
{
    public async Task<IReadOnlyList<ContentHistory>> GetForItemAsync(Guid contentItemId, CancellationToken ct = default) =>
        await db.Set<ContentHistory>()
            .AsNoTracking()
            .Where(x => x.ContentItemId == contentItemId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public void Add(ContentHistory entry) => db.Set<ContentHistory>().Add(entry);
}
