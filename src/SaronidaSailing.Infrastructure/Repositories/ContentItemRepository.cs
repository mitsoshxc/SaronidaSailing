using Microsoft.EntityFrameworkCore;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Repositories;

public sealed class ContentItemRepository(AppDbContext db) : IContentItemRepository
{
    public async Task<IReadOnlyList<ContentItem>> GetForPagesAsync(
        IReadOnlyCollection<string> pageKeys, IReadOnlyCollection<string> locales, CancellationToken ct = default) =>
        await db.Set<ContentItem>()
            .AsNoTracking()
            .Where(x => pageKeys.Contains(x.PageKey) && locales.Contains(x.Locale))
            .ToListAsync(ct);

    public Task<ContentItem?> FindStandaloneAsync(
        string pageKey, string sectionKey, string fieldKey, string locale, CancellationToken ct = default) =>
        db.Set<ContentItem>().FirstOrDefaultAsync(
            x => x.ListItemId == null
                && x.PageKey == pageKey
                && x.SectionKey == sectionKey
                && x.FieldKey == fieldKey
                && x.Locale == locale,
            ct);

    public Task<ContentItem?> FindListFieldAsync(
        Guid listItemId, string fieldKey, string locale, CancellationToken ct = default) =>
        db.Set<ContentItem>().FirstOrDefaultAsync(
            x => x.ListItemId == listItemId && x.FieldKey == fieldKey && x.Locale == locale,
            ct);

    public void Add(ContentItem item) => db.Set<ContentItem>().Add(item);
}
