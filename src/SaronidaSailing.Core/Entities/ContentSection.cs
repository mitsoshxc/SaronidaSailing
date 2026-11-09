namespace SaronidaSailing.Core.Entities;

public class ContentSection : BaseEntity
{
    private ContentSection()
    {
    }

    public required string PageKey { get; init; }

    public required string SectionKey { get; init; }

    public bool IsActive { get; private set; } = true;

    public static ContentSection Create(string pageKey, string sectionKey, bool isActive = true) =>
        new() { PageKey = pageKey, SectionKey = sectionKey, IsActive = isActive };

    public void SetActive(bool isActive) => IsActive = isActive;
}
