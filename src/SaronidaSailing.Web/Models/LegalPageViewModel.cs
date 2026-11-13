using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Models;

public sealed record LegalTocItem(string Href, string Title);

public sealed record LegalSeeAlso(string Path, string Name);

public sealed record LegalPageViewModel(
    string PageKey,
    IReadOnlyList<LegalTocItem> Toc,
    LegalSeeAlso? SeeAlso,
    string ContactPath)
{
    public const string SectionIdPrefix = "section-";

    public static string SectionsKey(string pageKey) => $"{pageKey}.sections";

    public static string BodyKey(string pageKey) => $"{pageKey}.body";

    public const string SectionTitleField = "title";

    public static IReadOnlyList<LegalTocItem> BuildToc(PageContent content, string pageKey, bool editor)
    {
        if (!editor && !content.IsSectionActive(BodyKey(pageKey)))
        {
            return [];
        }

        return content.GetList(SectionsKey(pageKey))
            .Where(e => editor || e.IsActive)
            .OrderBy(e => e.SortOrder)
            .Select(e => new LegalTocItem(
                $"#{SectionIdPrefix}{e.Id}",
                e.Fields.GetValueOrDefault(SectionTitleField)?.Value ?? ""))
            .Where(i => i.Title.Length > 0)
            .ToList();
    }
}
