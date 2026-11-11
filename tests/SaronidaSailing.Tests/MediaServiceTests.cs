using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Media;
using SaronidaSailing.Infrastructure.Repositories;
using SkiaSharp;
using Xunit.Abstractions;

namespace SaronidaSailing.Tests;

public sealed class MediaServiceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "saronida-media-" + Guid.NewGuid().ToString("N"));
    private readonly List<AppDbContext> _contexts = [];
    private string _connectionString = "";

    private string Root => Path.Combine(_folder, "media");

    private string Originals => Path.Combine(_folder, "originals");

    public async Task InitializeAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Originals);
    }

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        await PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Jpeg3000x2000_ProducesFourWidthsAndKeepsTheOriginal()
    {
        var bytes = MediaTestImages.Photo(3000, 2000);
        var stopwatch = Stopwatch.StartNew();

        var result = await NewService().ImportAsync(new MemoryStream(bytes), "Ιστιοπλοΐα Σαρωνίδα.JPG");

        stopwatch.Stop();
        output.WriteLine($"3000x2000 import took {stopwatch.ElapsedMilliseconds} ms");

        Assert.Null(result.Error);
        Assert.False(result.AlreadyExisted);
        var asset = result.Asset!;
        Assert.Equal([480, 960, 1600, 2400], asset.GetWidths());
        Assert.Equal(3000, asset.Width);
        Assert.Equal(2000, asset.Height);
        Assert.Equal("istioploia-saronida", asset.Slug);
        Assert.Equal("Ιστιοπλοΐα Σαρωνίδα.JPG", asset.OriginalFileName);
        Assert.Equal(".jpg", asset.OriginalFileExtension);
        Assert.Equal(Hash(bytes), asset.ContentHash);

        var expectedHeights = new Dictionary<int, int> { [480] = 320, [960] = 640, [1600] = 1067, [2400] = 1600 };
        foreach (var (width, height) in expectedHeights)
        {
            var path = Path.Combine(Root, MediaFileNames.PublicFileName(asset, width));
            Assert.True(File.Exists(path), path);
            Assert.Equal((width, height), MediaTestImages.SizeOf(path));
            using var codec = SKCodec.Create(path);
            Assert.Equal(SKEncodedImageFormat.Webp, codec!.EncodedFormat);
        }

        var originalPath = Path.Combine(Originals, asset.ContentHash + ".jpg");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(originalPath));
        Assert.Equal(4, Directory.GetFiles(Root).Length);
        Assert.Single(Directory.GetFiles(Originals));

        await using var verify = NewContext();
        var stored = await new MediaAssetRepository(verify).GetByIdAsync(asset.Id);
        Assert.NotNull(stored);
        Assert.Equal("480,960,1600,2400", stored.Widths);
        Assert.Equal(1, await CountRowsAsync());
    }

    [Fact]
    public async Task Png300x200_UsesTheImagesOwnWidthOnly()
    {
        var bytes = MediaTestImages.Photo(300, 200, SKEncodedImageFormat.Png);

        var result = await NewService().ImportAsync(new MemoryStream(bytes), "small.png");

        var asset = result.Asset!;
        Assert.Equal([300], asset.GetWidths());
        Assert.Equal(".png", asset.OriginalFileExtension);
        var path = Path.Combine(Root, MediaFileNames.PublicFileName(asset, 300));
        Assert.Equal((300, 200), MediaTestImages.SizeOf(path));
        Assert.Single(Directory.GetFiles(Root));
        Assert.True(File.Exists(Path.Combine(Originals, asset.ContentHash + ".png")));
    }

    [Fact]
    public async Task Image_JustWiderThanASize_NeverUpscales()
    {
        var bytes = MediaTestImages.Photo(1000, 500);

        var result = await NewService().ImportAsync(new MemoryStream(bytes), "p.jpg");

        Assert.Equal([480, 960], result.Asset!.GetWidths());
    }

    [Fact]
    public async Task PngWithTransparency_KeepsAlphaInTheWebp()
    {
        var bytes = MediaTestImages.HalfTransparentPng(600, 400);

        var result = await NewService().ImportAsync(new MemoryStream(bytes), "logo.png");

        var asset = result.Asset!;
        Assert.Equal([480], asset.GetWidths());
        using var bitmap = MediaTestImages.Decode(Path.Combine(Root, MediaFileNames.PublicFileName(asset, 480)));
        Assert.Equal(255, bitmap.GetPixel(40, 200).Alpha);
        Assert.True(bitmap.GetPixel(440, 200).Alpha < 10, "right half should stay transparent");
    }

    [Theory]
    [InlineData(3, 1200, 800, 1200, 800, "bottom-right")]
    [InlineData(6, 1200, 800, 800, 1200, "top-right")]
    [InlineData(8, 1200, 800, 800, 1200, "bottom-left")]
    public async Task ExifOrientation_IsAppliedAndNoMetadataSurvives(int orientation, int sourceWidth, int sourceHeight, int width, int height, string redCorner)
    {
        var bytes = MediaTestImages.WithExif(MediaTestImages.RedCornerJpeg(sourceWidth, sourceHeight), orientation);
        using (var sourceCodec = SKCodec.Create(SKData.CreateCopy(bytes)))
        {
            Assert.Equal(orientation, (int)sourceCodec!.EncodedOrigin);
        }

        var result = await NewService().ImportAsync(new MemoryStream(bytes), "rotated.jpg");

        var asset = result.Asset!;
        Assert.Equal(width, asset.Width);
        Assert.Equal(height, asset.Height);
        Assert.Equal(Hash(bytes), asset.ContentHash);

        var widths = asset.GetWidths();
        Assert.All(widths, w => Assert.True(w <= width));
        Assert.Equal(widths.Max(), widths[^1]);

        foreach (var w in widths)
        {
            var path = Path.Combine(Root, MediaFileNames.PublicFileName(asset, w));
            var (actualWidth, actualHeight) = MediaTestImages.SizeOf(path);
            Assert.Equal(w, actualWidth);
            Assert.Equal((int)Math.Round(height * (double)w / width), actualHeight);

            var file = await File.ReadAllBytesAsync(path);
            var text = Encoding.Latin1.GetString(file);
            Assert.DoesNotContain("Exif", text);
            Assert.DoesNotContain("EXIF", text);
            Assert.DoesNotContain("XMP", text);
            Assert.DoesNotContain("GPS", text);
            using var codec = SKCodec.Create(path);
            Assert.Equal(SKEncodedOrigin.TopLeft, codec!.EncodedOrigin);
        }

        var largest = Path.Combine(Root, MediaFileNames.PublicFileName(asset, widths[^1]));
        using var bitmap = MediaTestImages.Decode(largest);
        AssertRedOnlyInCorner(bitmap, redCorner);

        var original = await File.ReadAllBytesAsync(Path.Combine(Originals, asset.ContentHash + ".jpg"));
        Assert.Equal(bytes, original);
        Assert.Contains("Exif", Encoding.Latin1.GetString(original));
    }

    [Fact]
    public async Task StreamOverTheLimit_IsTooLarge_AndNotBufferedBeyondIt()
    {
        var stream = new ZeroStream(long.MaxValue);

        var result = await NewService().ImportAsync(stream, "huge.jpg");

        Assert.Equal(MediaImportError.TooLarge, result.Error);
        Assert.Null(result.Asset);
        Assert.InRange(stream.BytesRead, MediaLimits.MaxUploadBytes + 1, MediaLimits.MaxUploadBytes + 2 * 81920);
        AssertNothingWritten();
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task SeekableStreamOneByteOverTheLimit_IsTooLarge()
    {
        var result = await NewService().ImportAsync(new MemoryStream(new byte[MediaLimits.MaxUploadBytes + 1]), "huge.jpg");

        Assert.Equal(MediaImportError.TooLarge, result.Error);
        AssertNothingWritten();
    }

    [Fact]
    public async Task TextNamedJpg_IsNotAnImage()
    {
        var result = await NewService().ImportAsync(new MemoryStream("this is not a picture"u8.ToArray()), "x.jpg");

        Assert.Equal(MediaImportError.NotAnImage, result.Error);
        AssertNothingWritten();
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task EmptyStream_IsNotAnImage()
    {
        var result = await NewService().ImportAsync(new MemoryStream(), "empty.jpg");

        Assert.Equal(MediaImportError.NotAnImage, result.Error);
    }

    [Fact]
    public async Task TruncatedJpeg_IsNotAnImage()
    {
        var bytes = MediaTestImages.Photo(800, 600);

        var result = await NewService().ImportAsync(new MemoryStream(bytes[..(bytes.Length / 2)]), "cut.jpg");

        Assert.Equal(MediaImportError.NotAnImage, result.Error);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Gif_IsUnsupportedFormat()
    {
        var result = await NewService().ImportAsync(new MemoryStream(MediaTestImages.TinyGif), "x.gif");

        Assert.Equal(MediaImportError.UnsupportedFormat, result.Error);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("heix")]
    [InlineData("mif1")]
    public async Task IsoBmffHeicBrand_IsHeic(string brand)
    {
        byte[] bytes =
        [
            0x00, 0x00, 0x00, 0x18, .. "ftyp"u8.ToArray(), .. Encoding.ASCII.GetBytes(brand),
            0x00, 0x00, 0x00, 0x00, .. "mif1heic"u8.ToArray(),
        ];

        var result = await NewService().ImportAsync(new MemoryStream(bytes), "IMG_0001.HEIC");

        Assert.Equal(MediaImportError.Heic, result.Error);
        AssertNothingWritten();
    }

    [Fact]
    public async Task PngClaiming8000x6300_IsTooManyPixels_WithoutDecoding()
    {
        var result = await NewService().ImportAsync(new MemoryStream(MediaTestImages.PngClaiming(8000, 6300)), "big.png");

        Assert.Equal(MediaImportError.TooManyPixels, result.Error);
        AssertNothingWritten();
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task SameBytesTwice_ReturnsTheSameAsset_WithoutNewFiles()
    {
        var bytes = MediaTestImages.Photo(1200, 800);

        var first = await NewService().ImportAsync(new MemoryStream(bytes), "first.jpg");
        var filesAfterFirst = ListFiles();
        var second = await NewService().ImportAsync(new MemoryStream(bytes), "other-name.jpg");

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Asset!.Id, second.Asset!.Id);
        Assert.Equal("first.jpg", second.Asset.OriginalFileName);
        Assert.Equal(filesAfterFirst, ListFiles());
        Assert.Equal(1, await CountRowsAsync());
    }

    [Fact]
    public async Task ExistingHash_DoesNotNeedTheGate()
    {
        var bytes = MediaTestImages.Photo(1200, 800);
        await NewService().ImportAsync(new MemoryStream(bytes), "first.jpg");
        var gate = new MediaProcessingGate();
        await gate.WaitAsync(TimeSpan.Zero);

        var result = await NewService(gate).ImportAsync(new MemoryStream(bytes), "again.jpg");

        Assert.True(result.AlreadyExisted);
    }

    [Fact]
    public async Task TwoInstancesImportingTheSameBytesConcurrently_ProduceOneRow()
    {
        var bytes = MediaTestImages.Photo(2000, 1300);
        var first = NewService(new MediaProcessingGate());
        var second = NewService(new MediaProcessingGate());

        var results = await Task.WhenAll(
            Task.Run(() => first.ImportAsync(new MemoryStream(bytes), "a.jpg")),
            Task.Run(() => second.ImportAsync(new MemoryStream(bytes), "b.jpg")));

        Assert.All(results, r => Assert.Null(r.Error));
        Assert.Equal(results[0].Asset!.Id, results[1].Asset!.Id);
        Assert.Equal(1, await CountRowsAsync());
        AssertNoTempFiles();
    }

    [Fact]
    public async Task LosingTheInsertRace_ReturnsTheWinnersRow()
    {
        var bytes = MediaTestImages.Photo(1200, 800);
        var winner = await NewService().ImportAsync(new MemoryStream(bytes), "winner.jpg");
        var staleCheck = NewService(wrap: repository => new StaleFirstLookupRepository(repository));

        var loser = await staleCheck.ImportAsync(new MemoryStream(bytes), "loser.jpg");

        Assert.True(loser.AlreadyExisted);
        Assert.Equal(winner.Asset!.Id, loser.Asset!.Id);
        Assert.Equal("winner.jpg", loser.Asset.OriginalFileName);
        Assert.Equal(1, await CountRowsAsync());
        AssertNoTempFiles();
    }

    [Fact]
    public async Task GateHeldByAnotherImport_ReturnsBusy()
    {
        var gate = new MediaProcessingGate();
        Assert.True(await gate.WaitAsync(TimeSpan.Zero));
        var service = NewService(gate, waitSeconds: 0);

        var result = await service.ImportAsync(new MemoryStream(MediaTestImages.Photo(800, 600)), "busy.jpg");

        Assert.Equal(MediaImportError.Busy, result.Error);
        AssertNothingWritten();
        Assert.Equal(0, await CountRowsAsync());

        gate.Release();
        var retry = await service.ImportAsync(new MemoryStream(MediaTestImages.Photo(800, 600)), "busy.jpg");
        Assert.Null(retry.Error);
    }

    [Fact]
    public async Task WaitingImport_ProceedsWhenTheGateIsReleased()
    {
        var gate = new MediaProcessingGate();
        Assert.True(await gate.WaitAsync(TimeSpan.Zero));
        var service = NewService(gate, waitSeconds: 30);

        var pending = service.ImportAsync(new MemoryStream(MediaTestImages.Photo(800, 600)), "wait.jpg");
        await Task.Delay(200);
        Assert.False(pending.IsCompleted);
        gate.Release();

        Assert.Null((await pending).Error);
    }

    [Fact]
    public async Task SuccessfulImport_LeavesNoTempFiles()
    {
        await NewService().ImportAsync(new MemoryStream(MediaTestImages.Photo(1800, 1200)), "ok.jpg");

        AssertNoTempFiles();
        Assert.All(Directory.GetFiles(Root), f => Assert.EndsWith(".webp", f));
    }

    [Fact]
    public async Task FailedWrite_LeavesNoTempFilesAndNoRow_AndReleasesTheGate()
    {
        var bytes = MediaTestImages.Photo(1800, 1200);
        Directory.CreateDirectory(Path.Combine(Originals, Hash(bytes) + ".jpg"));
        var gate = new MediaProcessingGate();
        var service = NewService(gate);

        await Assert.ThrowsAnyAsync<Exception>(() => service.ImportAsync(new MemoryStream(bytes), "fail.jpg"));

        AssertNoTempFiles();
        Assert.Equal(0, await CountRowsAsync());
        Assert.True(await gate.WaitAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task ImportingTwoDifferentImages_StoresTwoAssets()
    {
        var service = NewService();

        var first = await service.ImportAsync(new MemoryStream(MediaTestImages.Photo(1000, 700, seed: 1)), "a.jpg");
        var second = await service.ImportAsync(new MemoryStream(MediaTestImages.Photo(1000, 700, seed: 2)), "a.jpg");

        Assert.NotEqual(first.Asset!.Id, second.Asset!.Id);
        Assert.NotEqual(first.Asset.ContentHash, second.Asset.ContentHash);
        Assert.Equal(2, await CountRowsAsync());
    }

    private MediaService NewService(MediaProcessingGate? gate = null, int waitSeconds = 30, Func<IMediaAssetRepository, IMediaAssetRepository>? wrap = null)
    {
        var context = NewContext();
        _contexts.Add(context);
        IMediaAssetRepository repository = new MediaAssetRepository(context);
        if (wrap is not null)
        {
            repository = wrap(repository);
        }

        var options = Microsoft.Extensions.Options.Options.Create(new MediaOptions
        {
            RootPath = Root,
            OriginalsPath = Originals,
            ProcessingWaitSeconds = waitSeconds,
        });

        return new MediaService(repository, new UnitOfWork(context), options, gate ?? new MediaProcessingGate());
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options, TimeProvider.System);

    [Fact]
    public async Task WriteAtomic_FinalFileAlreadyExists_SkipsWriteAndLeavesItUntouched()
    {
        var finalPath = Path.Combine(Root, "a.webp");
        await File.WriteAllBytesAsync(finalPath, [1, 2, 3]);

        await MediaService.WriteAtomicAsync(Root, "a.webp", new byte[] { 9, 9 }, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(finalPath));
        AssertNoTempFiles();
        Assert.Single(Directory.GetFiles(Root));
    }

    [Fact]
    public async Task WriteAtomic_LosingMove_TreatsAnotherWritersFileAsSuccess()
    {
        var finalPath = Path.Combine(Root, "b.webp");

        await MediaService.WriteAtomicAsync(Root, "b.webp", new byte[] { 9, 9 }, CancellationToken.None, (_, final) =>
        {
            File.WriteAllBytes(final, [1, 2, 3]);
            throw new UnauthorizedAccessException();
        });

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(finalPath));
        AssertNoTempFiles();
    }

    [Fact]
    public async Task WriteAtomic_MoveFailsWithoutFinalFile_Rethrows_AndLeavesNoTemp()
    {
        await Assert.ThrowsAsync<IOException>(() => MediaService.WriteAtomicAsync(
            Root, "c.webp", new byte[] { 9 }, CancellationToken.None, (_, _) => throw new IOException()));

        AssertNoTempFiles();
        Assert.Empty(Directory.GetFiles(Root));
    }

    private async Task<int> CountRowsAsync()
    {
        await using var context = NewContext();
        return await context.Set<MediaAsset>().CountAsync();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private List<string> ListFiles() =>
        Directory.GetFiles(_folder, "*", SearchOption.AllDirectories).Order().ToList();

    private void AssertNothingWritten() => Assert.Empty(ListFiles());

    private void AssertNoTempFiles() =>
        Assert.DoesNotContain(ListFiles(), f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

    private static void AssertRedOnlyInCorner(SKBitmap bitmap, string corner)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var points = new Dictionary<string, SKColor>
        {
            ["top-left"] = bitmap.GetPixel((int)(w * 0.1), (int)(h * 0.1)),
            ["top-right"] = bitmap.GetPixel((int)(w * 0.9), (int)(h * 0.1)),
            ["bottom-left"] = bitmap.GetPixel((int)(w * 0.1), (int)(h * 0.9)),
            ["bottom-right"] = bitmap.GetPixel((int)(w * 0.9), (int)(h * 0.9)),
        };

        foreach (var (name, color) in points)
        {
            var isRed = color.Red > 180 && color.Blue < 100;
            Assert.True(isRed == (name == corner), $"{name} pixel was {color}, expected red only at {corner}");
        }
    }

    private sealed class ZeroStream(long length) : Stream
    {
        private long _remaining = length;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, _remaining);
            buffer[..count].Clear();
            _remaining -= count;
            BytesRead += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StaleFirstLookupRepository(IMediaAssetRepository inner) : IMediaAssetRepository
    {
        private bool _lookedUp;

        public Task<MediaAsset?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<MediaAsset>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            inner.GetByIdsAsync(ids, ct);

        public Task<MediaAsset?> GetByContentHashAsync(string contentHash, CancellationToken ct = default)
        {
            if (_lookedUp)
            {
                return inner.GetByContentHashAsync(contentHash, ct);
            }

            _lookedUp = true;
            return Task.FromResult<MediaAsset?>(null);
        }

        public Task<IReadOnlyList<MediaAsset>> ListNewestFirstAsync(int skip, int take, CancellationToken ct = default) =>
            inner.ListNewestFirstAsync(skip, take, ct);

        public Task<IReadOnlySet<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            inner.ExistAsync(ids, ct);

        public void Add(MediaAsset asset) => inner.Add(asset);
    }
}
