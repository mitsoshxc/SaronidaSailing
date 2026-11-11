using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Core.Interfaces;

public record PageImageSource(int Width, string Url);

public record PageImage(Guid Id, int Width, int Height, IReadOnlyList<PageImageSource> Sources)
{
    public int HeightAt(int width) => MediaDimensions.ScaledHeight(Width, Height, width);
}
