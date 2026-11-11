using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace SaronidaSailing.Tests;

internal static class MediaTestImages
{
    public static readonly byte[] TinyGif =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
    ];

    public static byte[] Photo(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg, int seed = 1)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(width, height), [new SKColor(20, 90, 160), new SKColor(240, 200, 80)], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
        {
            canvas.DrawRect(0, 0, width, height, paint);
            var random = new Random(seed);
            using var spot = new SKPaint { IsAntialias = true };
            for (var i = 0; i < 150; i++)
            {
                spot.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 200);
                canvas.DrawCircle(random.Next(width), random.Next(height), random.Next(5, Math.Max(6, width / 20)), spot);
            }
        }

        return Encode(bitmap, format);
    }

    public static byte[] RedCornerJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0, 0, 255));
            using var red = new SKPaint { Color = new SKColor(255, 0, 0) };
            canvas.DrawRect(0, 0, width * 0.3f, height * 0.3f, red);
        }

        return Encode(bitmap, SKEncodedImageFormat.Jpeg);
    }

    public static byte[] HalfTransparentPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = new SKColor(200, 30, 30) };
            canvas.DrawRect(0, 0, width / 2f, height, paint);
        }

        return Encode(bitmap, SKEncodedImageFormat.Png);
    }

    public static byte[] WithExif(byte[] jpeg, int orientation)
    {
        var exif = BuildExif(orientation);
        var insertAt = 2;
        if (jpeg[2] == 0xFF && jpeg[3] == 0xE0)
        {
            insertAt = 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4));
        }

        var segment = new List<byte> { 0xFF, 0xE1 };
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(exif.Length + 2));
        segment.AddRange(length);
        segment.AddRange(exif);

        return [.. jpeg.AsSpan(0, insertAt), .. segment, .. jpeg.AsSpan(insertAt)];
    }

    public static byte[] PngClaiming(int width, int height)
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        AddChunk(png, "IHDR", ihdr);
        AddChunk(png, "IDAT", []);
        AddChunk(png, "IEND", []);

        return [.. png];
    }

    public static SKBitmap Decode(string path)
    {
        using var data = SKData.Create(path);
        return SKBitmap.Decode(data) ?? throw new InvalidOperationException($"{path} is not a decodable image.");
    }

    public static (int Width, int Height) SizeOf(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidOperationException($"{path} is not a decodable image.");
        return (codec.Info.Width, codec.Info.Height);
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 92) ?? throw new InvalidOperationException("Encoding failed.");
        return data.ToArray();
    }

    private static byte[] BuildExif(int orientation)
    {
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        AddU16(tiff, 42);
        AddU32(tiff, 8);

        AddU16(tiff, 2);
        AddEntry(tiff, 0x0112, 3, 1, (uint)orientation);
        AddEntry(tiff, 0x8825, 4, 1, 38);
        AddU32(tiff, 0);

        AddU16(tiff, 4);
        AddEntry(tiff, 0x0001, 2, 2, 'N');
        AddEntry(tiff, 0x0002, 5, 3, 92);
        AddEntry(tiff, 0x0003, 2, 2, 'E');
        AddEntry(tiff, 0x0004, 5, 3, 116);
        AddU32(tiff, 0);

        foreach (var (numerator, denominator) in new (uint, uint)[] { (37, 1), (58, 1), (1234, 100), (23, 1), (43, 1), (4321, 100) })
        {
            AddU32(tiff, numerator);
            AddU32(tiff, denominator);
        }

        return [.. "Exif\0\0"u8.ToArray(), .. tiff];
    }

    private static void AddEntry(List<byte> bytes, ushort tag, ushort type, uint count, uint value)
    {
        AddU16(bytes, tag);
        AddU16(bytes, type);
        AddU32(bytes, count);
        if (type == 3)
        {
            AddU16(bytes, (ushort)value);
            AddU16(bytes, 0);
        }
        else
        {
            AddU32(bytes, value);
        }
    }

    private static void AddU16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private static void AddU32(List<byte> bytes, uint value)
    {
        AddU16(bytes, (ushort)value);
        AddU16(bytes, (ushort)(value >> 16));
    }

    private static void AddChunk(List<byte> png, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        png.AddRange(length);
        png.AddRange(typeBytes);
        png.AddRange(data);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. typeBytes, .. data]));
        png.AddRange(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
