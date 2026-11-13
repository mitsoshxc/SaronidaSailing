namespace SaronidaSailing.Core.Entities;

public class InstagramPost : BaseEntity
{
    private InstagramPost()
    {
    }

    public required string InstagramId { get; init; }

    public required string Permalink { get; init; }

    public string? Caption { get; init; }

    public required string MediaType { get; init; }

    public required Guid MediaAssetId { get; init; }

    public required DateTime PostedAt { get; init; }

    public static InstagramPost Create(
        string instagramId,
        string permalink,
        string? caption,
        string mediaType,
        Guid mediaAssetId,
        DateTime postedAt,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.CreateVersion7(),
            InstagramId = instagramId,
            Permalink = permalink,
            Caption = caption,
            MediaType = mediaType,
            MediaAssetId = mediaAssetId,
            PostedAt = postedAt,
        };
}
