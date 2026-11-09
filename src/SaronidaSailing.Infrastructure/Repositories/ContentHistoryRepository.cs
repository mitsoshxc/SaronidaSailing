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

    public async Task<IReadOnlyList<ContentHistory>> GetForFieldAsync(
        string pageKey, string sectionKey, string fieldKey, string locale, Guid? listItemId, CancellationToken ct = default) =>
        await db.Set<ContentHistory>()
            .AsNoTracking()
            .Join(
                db.Set<ContentItem>(),
                history => history.ContentItemId,
                item => item.Id,
                (history, item) => new { history, item })
            .Where(x => x.item.PageKey == pageKey
                && x.item.SectionKey == sectionKey
                && x.item.FieldKey == fieldKey
                && x.item.Locale == locale
                && x.item.ListItemId == listItemId)
            .OrderByDescending(x => x.history.CreatedAt).ThenByDescending(x => x.history.Id)
            .Select(x => x.history)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<HistoryEntry>> GetForSaveAsync(Guid saveId, CancellationToken ct = default)
    {
        var rows = await db.Set<ContentHistory>()
            .AsNoTracking()
            .Where(x => x.SaveId == saveId)
            .Join(
                db.Set<ContentItem>(),
                history => history.ContentItemId,
                item => item.Id,
                (history, item) => new { history, item })
            .OrderBy(x => x.history.CreatedAt).ThenBy(x => x.history.Id)
            .ToListAsync(ct);
        return [.. rows.Select(x => new HistoryEntry(x.history, x.item))];
    }

    public async Task<IReadOnlyList<ContentHistory>> GetForItemsAsync(
        IReadOnlyCollection<Guid> contentItemIds, CancellationToken ct = default) =>
        await db.Set<ContentHistory>()
            .AsNoTracking()
            .Where(x => contentItemIds.Contains(x.ContentItemId))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public void Add(ContentHistory entry) => db.Set<ContentHistory>().Add(entry);
}
