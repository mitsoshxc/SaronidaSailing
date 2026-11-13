using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Core.Interfaces;

public record PageImageSource(int Width, string Url);

public record PageImage(Guid Id, int Width, int Height, IReadOnlyList<PageImageSource> Sources)
{
    public int HeightAt(int width) => MediaDimensions.ScaledHeight(Width, Height, width);

    public PageImageSource? SourceAt(int width) => Sources.FirstOrDefault(s => s.Width == width);

    public string SrcSet => string.Join(", ", Sources.Select(s => $"{s.Url} {s.Width}w"));

    public static PageImage From(MediaAsset asset) =>
        new(
            asset.Id,
            asset.Width,
            asset.Height,
            [.. asset.GetWidths().Order().Select(w => new PageImageSource(w, MediaFileNames.PublicUrl(asset, w)))]);
}
