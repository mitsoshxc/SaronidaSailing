using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Infrastructure.Media;

public sealed class MediaService(
    IMediaAssetRepository assets,
    IUnitOfWork unitOfWork,
    IOptions<MediaOptions> options,
    MediaProcessingGate gate) : IMediaService
{
    private const int MaxFileNameLength = 255;
    private const string UniqueViolation = "23505";
    private const string ContentHashIndex = "UX_MediaAssets_ContentHash";

    public async Task<MediaImportResult> ImportAsync(Stream content, string originalFileName, CancellationToken ct = default)
    {
        var bytes = await ReadLimitedAsync(content, ct);
        if (bytes is null)
        {
            return MediaImportResult.Failed(MediaImportError.TooLarge);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes.Value.Span));
        var existing = await assets.GetByContentHashAsync(hash, ct);
        if (existing is not null)
        {
            return MediaImportResult.Existing(existing);
        }

        var slug = Slugs.FromFileName(originalFileName);
        ProcessedImage image;

        if (!await gate.WaitAsync(TimeSpan.FromSeconds(options.Value.ProcessingWaitSeconds), ct))
        {
            return MediaImportResult.Failed(MediaImportError.Busy);
        }

        try
        {
            var processed = await Task.Run(() => ImageProcessor.Process(bytes.Value), ct);
            if (processed.Error is { } error)
            {
                return MediaImportResult.Failed(error);
            }

            image = processed.Image!;
            await WriteFilesAsync(slug, hash, bytes.Value, image, ct);
        }
        finally
        {
            gate.Release();
        }

        var asset = MediaAsset.Create(
            Truncate(originalFileName),
            hash,
            slug,
            image.Variants.Select(v => v.Width).ToList(),
            image.Width,
            image.Height,
            image.OriginalExtension);

        assets.Add(asset);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsContentHashViolation(ex))
        {
            unitOfWork.DiscardChanges();
            var winner = await assets.GetByContentHashAsync(hash, ct) ?? throw new InvalidOperationException(
                "A unique violation on the content hash was reported, but no row with that hash exists.", ex);
            return MediaImportResult.Existing(winner);
        }

        return MediaImportResult.Imported(asset);
    }

    private async Task WriteFilesAsync(string slug, string hash, ReadOnlyMemory<byte> original, ProcessedImage image, CancellationToken ct)
    {
        foreach (var variant in image.Variants)
        {
            await WriteAtomicAsync(options.Value.RootPath, MediaFileNames.PublicFileName(slug, hash, variant.Width), variant.Webp, ct);
        }

        await WriteAtomicAsync(options.Value.OriginalsPath, MediaFileNames.OriginalFileName(hash, image.OriginalExtension), original, ct);
    }

    private static async Task WriteAtomicAsync(string folder, string fileName, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var finalPath = Path.Combine(folder, fileName);
        var tempPath = Path.Combine(folder, $"{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, ct);
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    private static async Task<ReadOnlyMemory<byte>?> ReadLimitedAsync(Stream content, CancellationToken ct)
    {
        if (content.CanSeek && content.Length - content.Position > MediaLimits.MaxUploadBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MediaLimits.MaxUploadBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static bool IsContentHashViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: ContentHashIndex };

    private static string Truncate(string fileName) =>
        fileName.Length <= MaxFileNameLength ? fileName : fileName[..MaxFileNameLength];
}
