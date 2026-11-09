using Microsoft.EntityFrameworkCore;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Repositories;

public sealed class ContentSectionRepository(AppDbContext db) : IContentSectionRepository
{
    public async Task<IReadOnlyList<ContentSection>> GetForPagesAsync(
        IReadOnlyCollection<string> pageKeys, CancellationToken ct = default) =>
        await db.Set<ContentSection>()
            .AsNoTracking()
            .Where(x => pageKeys.Contains(x.PageKey))
            .ToListAsync(ct);

    public Task<ContentSection?> FindAsync(string pageKey, string sectionKey, CancellationToken ct = default) =>
        db.Set<ContentSection>().FirstOrDefaultAsync(x => x.PageKey == pageKey && x.SectionKey == sectionKey, ct);

    public void Add(ContentSection section) => db.Set<ContentSection>().Add(section);
}
