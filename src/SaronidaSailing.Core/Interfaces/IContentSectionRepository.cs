using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public interface IContentSectionRepository
{
    Task<IReadOnlyList<ContentSection>> GetForPagesAsync(
        IReadOnlyCollection<string> pageKeys, CancellationToken ct = default);

    Task<ContentSection?> FindAsync(string pageKey, string sectionKey, CancellationToken ct = default);

    void Add(ContentSection section);
}
