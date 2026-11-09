using Microsoft.EntityFrameworkCore;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Repositories;

public sealed class ContentListItemRepository(AppDbContext db) : IContentListItemRepository
{
    public async Task<IReadOnlyList<ContentListItem>> GetForPagesAsync(
        IReadOnlyCollection<string> pageKeys, CancellationToken ct = default) =>
        await db.Set<ContentListItem>()
            .AsNoTracking()
            .Where(x => pageKeys.Contains(x.PageKey))
            .OrderBy(x => x.PageKey).ThenBy(x => x.ListKey).ThenBy(x => x.SortOrder)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContentListItem>> GetListAsync(
        string pageKey, string listKey, CancellationToken ct = default) =>
        await db.Set<ContentListItem>()
            .Where(x => x.PageKey == pageKey && x.ListKey == listKey)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

    public Task<ContentListItem?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Set<ContentListItem>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(ContentListItem item) => db.Set<ContentListItem>().Add(item);

    public void Remove(ContentListItem item) => db.Set<ContentListItem>().Remove(item);
}
