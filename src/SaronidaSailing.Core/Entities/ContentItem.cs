namespace SaronidaSailing.Core.Entities;

public class ContentItem : BaseEntity
{
    private ContentItem()
    {
    }

    public required string PageKey { get; init; }

    public required string SectionKey { get; init; }

    public required string FieldKey { get; init; }

    public required string Locale { get; init; }

    public Guid? ListItemId { get; init; }

    public required ContentType Type { get; init; }

    public string Value { get; private set; } = "";

    public string UpdatedBy { get; private set; } = "";

    public ContentListItem? ListItem { get; init; }

    public static ContentItem Create(
        string pageKey,
        string sectionKey,
        string fieldKey,
        string locale,
        Guid? listItemId,
        ContentType type,
        string value,
        string editorEmail) =>
        new()
        {
            PageKey = pageKey,
            SectionKey = sectionKey,
            FieldKey = fieldKey,
            Locale = locale,
            ListItemId = listItemId,
            Type = type,
            Value = value,
            UpdatedBy = editorEmail
        };

    public void SetValue(string value, string editorEmail)
    {
        Value = value;
        UpdatedBy = editorEmail;
    }
}
