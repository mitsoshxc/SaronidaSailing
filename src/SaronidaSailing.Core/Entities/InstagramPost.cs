namespace SaronidaSailing.Core.Entities;

public class InstagramPost : BaseEntity
{
    private InstagramPost()
    {
    }

    public required string InstagramId { get; init; }

    public string Permalink { get; private set; } = "";

    public string? Caption { get; private set; }

    public required string MediaType { get; init; }

    public required Guid MediaAssetId { get; init; }

    public required DateTime PostedAt { get; init; }

    public bool Update(string? caption, string permalink)
    {
        if (Caption == caption && Permalink == permalink)
        {
            return false;
        }

        Caption = caption;
        Permalink = permalink;
        return true;
    }

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
