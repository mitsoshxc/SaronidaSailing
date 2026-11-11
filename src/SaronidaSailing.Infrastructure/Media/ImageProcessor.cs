using System.Text;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SkiaSharp;

namespace SaronidaSailing.Infrastructure.Media;

internal sealed record ProcessedVariant(int Width, byte[] Webp);

internal sealed record ProcessedImage(int Width, int Height, string OriginalExtension, IReadOnlyList<ProcessedVariant> Variants);

internal sealed record ProcessingResult(ProcessedImage? Image, MediaImportError? Error)
{
    public static ProcessingResult Success(ProcessedImage image) => new(image, null);

    public static ProcessingResult Failure(MediaImportError error) => new(null, error);
}

internal static class ImageProcessor
{
    private static readonly byte[][] HeicBrands =
        new[] { "heic", "heix", "hevc", "hevx", "mif1", "msf1" }.Select(Encoding.ASCII.GetBytes).ToArray();

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public static ProcessingResult Process(ReadOnlyMemory<byte> bytes)
    {
        using var data = SKData.CreateCopy(bytes.Span);
        using var codec = SKCodec.Create(data);

        if (codec is null)
        {
            return ProcessingResult.Failure(IsHeic(bytes.Span) ? MediaImportError.Heic : MediaImportError.NotAnImage);
        }

        if (codec.EncodedFormat == SKEncodedImageFormat.Heif)
        {
            return ProcessingResult.Failure(MediaImportError.Heic);
        }

        var extension = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ".jpg",
            SKEncodedImageFormat.Png => ".png",
            SKEncodedImageFormat.Webp => ".webp",
            _ => null,
        };

        if (extension is null)
        {
            return ProcessingResult.Failure(MediaImportError.UnsupportedFormat);
        }

        var sourceWidth = codec.Info.Width;
        var sourceHeight = codec.Info.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return ProcessingResult.Failure(MediaImportError.NotAnImage);
        }

        if ((long)sourceWidth * sourceHeight > MediaLimits.MaxPixels)
        {
            return ProcessingResult.Failure(MediaImportError.TooManyPixels);
        }

        var info = new SKImageInfo(sourceWidth, sourceHeight, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            return ProcessingResult.Failure(MediaImportError.NotAnImage);
        }

        bitmap.SetImmutable();
        using var image = SKImage.FromBitmap(bitmap);

        var origin = codec.EncodedOrigin;
        var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapped ? sourceHeight : sourceWidth;
        var height = swapped ? sourceWidth : sourceHeight;
        var originMatrix = OriginMatrix(origin, sourceWidth, sourceHeight);

        var widths = MediaLimits.Widths.Where(w => w <= width).ToList();
        if (widths.Count == 0)
        {
            widths.Add(width);
        }

        var variants = new List<ProcessedVariant>(widths.Count);
        foreach (var targetWidth in widths)
        {
            variants.Add(new ProcessedVariant(targetWidth, Resize(image, originMatrix, width, height, targetWidth)));
        }

        return ProcessingResult.Success(new ProcessedImage(width, height, extension, variants));
    }

    private static byte[] Resize(SKImage image, SKMatrix originMatrix, int width, int height, int targetWidth)
    {
        var targetHeight = MediaDimensions.ScaledHeight(width, height, targetWidth);
        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());

        using var target = new SKBitmap(info);
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)targetWidth / width, (float)targetHeight / height);
            canvas.Concat(in originMatrix);
            canvas.DrawImage(image, 0, 0, Sampling);
        }

        using var pixmap = target.PeekPixels();
        using var encoded = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, MediaLimits.WebpQuality));
        return (encoded ?? throw new InvalidOperationException("WebP encoding failed.")).ToArray();
    }

    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    private static bool IsHeic(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return false;
        }

        var brand = bytes.Slice(8, 4);
        foreach (var heicBrand in HeicBrands)
        {
            if (brand.SequenceEqual(heicBrand))
            {
                return true;
            }
        }

        return false;
    }
}
