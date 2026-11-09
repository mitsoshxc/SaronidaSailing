namespace SaronidaSailing.Core.Interfaces;

public record ContentEditor(string Email, string? IpAddress);

public record ContentSaveResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static ContentSaveResult Success { get; } = new(true, []);

    public static ContentSaveResult Failure(params string[] errors) => new(false, errors);
}

public interface IContentService
{
    Task<PageContent> GetPageContentAsync(string pageKey, string locale, CancellationToken ct = default);

    Task<ContentSaveResult> ApplyChangesAsync(
        IReadOnlyList<ContentChange> changes, ContentEditor editor, CancellationToken ct = default);
}
