using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public record ContentEditor(string Email, string? IpAddress);

public record ContentSaveResult(bool Succeeded, IReadOnlyList<string> Errors, Guid? SaveId = null)
{
    public static ContentSaveResult Success { get; } = new(true, []);

    public static ContentSaveResult Saved(Guid saveId) => new(true, [], saveId);

    public static ContentSaveResult Failure(params string[] errors) => new(false, errors);
}

public record FieldVersion(Guid SaveId, string Value, string ChangedBy, DateTime CreatedAt);

public record SavedField(
    string Key, string Locale, ContentType Type, Guid? ListItemId, string Value, string PreviousValue, string CurrentValue);

public record ContentSaveDetails(Guid SaveId, string ChangedBy, DateTime CreatedAt, IReadOnlyList<SavedField> Fields);

public interface IContentService
{
    Task<PageContent> GetPageContentAsync(string pageKey, string locale, CancellationToken ct = default);

    Task<ContentSaveResult> ApplyChangesAsync(
        IReadOnlyList<ContentChange> changes, ContentEditor editor, CancellationToken ct = default);

    Task<IReadOnlyList<FieldVersion>> GetFieldHistoryAsync(
        string key, string locale, Guid? listItemId, CancellationToken ct = default);

    Task<ContentSaveDetails?> GetSaveAsync(Guid saveId, CancellationToken ct = default);
}
