namespace SaronidaSailing.Core.Entities;

public class MediaAsset : BaseEntity
{
    private MediaAsset()
    {
    }

    public required string OriginalFileName { get; init; }

    public required string ContentHash { get; init; }

    public required string Slug { get; init; }

    public required string Widths { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required string OriginalFileExtension { get; init; }

    public static MediaAsset Create(
        string originalFileName,
        string contentHash,
        string slug,
        IReadOnlyCollection<int> widths,
        int width,
        int height,
        string originalFileExtension,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.CreateVersion7(),
            OriginalFileName = originalFileName.Trim(),
            ContentHash = contentHash,
            Slug = slug,
            Widths = string.Join(',', widths.Order()),
            Width = width,
            Height = height,
            OriginalFileExtension = originalFileExtension,
        };

    public IReadOnlyList<int> GetWidths() =>
        Widths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToList();
}
