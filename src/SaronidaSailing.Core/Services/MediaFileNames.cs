using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Services;

public static class MediaFileNames
{
    public const string PublicUrlPrefix = "/media/";

    private const int HashLengthInName = 16;

    public static string PublicFileName(string slug, string contentHash, int width) =>
        $"{slug}-{contentHash[..HashLengthInName]}-{width}.webp";

    public static string PublicFileName(MediaAsset asset, int width) =>
        PublicFileName(asset.Slug, asset.ContentHash, width);

    public static string PublicUrl(string slug, string contentHash, int width) =>
        PublicUrlPrefix + PublicFileName(slug, contentHash, width);

    public static string PublicUrl(MediaAsset asset, int width) =>
        PublicUrl(asset.Slug, asset.ContentHash, width);

    public static string OriginalFileName(string contentHash, string originalFileExtension) =>
        contentHash + originalFileExtension;

    public static string OriginalFileName(MediaAsset asset) =>
        OriginalFileName(asset.ContentHash, asset.OriginalFileExtension);
}
