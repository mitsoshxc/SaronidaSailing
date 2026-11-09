namespace SaronidaSailing.Core.Entities;

public class ContentListItem : BaseEntity
{
    private ContentListItem()
    {
    }

    public required string PageKey { get; init; }

    public required string ListKey { get; init; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public ICollection<ContentItem> Fields { get; init; } = [];

    public static ContentListItem Create(string pageKey, string listKey, int sortOrder) =>
        new() { PageKey = pageKey, ListKey = listKey, SortOrder = sortOrder };

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public void SetActive(bool isActive) => IsActive = isActive;
}
