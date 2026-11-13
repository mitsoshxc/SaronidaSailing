using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Identity;

namespace SaronidaSailing.Web.Endpoints;

public static class MediaEndpoints
{
    public const int MaxBodyBytes = MediaLimits.MaxUploadBytes + 64 * 1024;
    public const int PageSize = 48;

    private const int PreviewWidth = 960;
    private const int MaxPage = int.MaxValue / PageSize;
    private const string UploadField = "file";
    private const string TooLargeMessage = "The file is too large. The maximum is 25 MB.";
    private const string NoFileMessage = "Send exactly one image in the 'file' form field.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/api/media")
            .RequireAuthorization(AdminPolicy.Name)
            .AddEndpointFilter(NoStore);

        group.MapPost("", UploadAsync);
        group.MapGet("", ListAsync);
        group.MapGet("{id:guid}", GetAsync);

        return app;
    }

    private static ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return next(context);
    }

    private static async Task<IResult> UploadAsync(
        HttpContext http, IAntiforgery antiforgery, IMediaService media, IAuditLog audit, CancellationToken ct)
    {
        var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Error(StatusCodes.Status400BadRequest, "invalidAntiforgery", "Invalid antiforgery token.");
        }

        var userName = http.User.Identity?.Name;
        var ip = http.Connection.RemoteIpAddress?.ToString();

        IResult Reject(int status, string code, string message)
        {
            audit.Record("media-rejected", userName, ip, $"code={code}");
            return Error(status, code, message);
        }

        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return Reject(StatusCodes.Status413PayloadTooLarge, "tooLarge", TooLargeMessage);
        }

        if (!http.Request.HasFormContentType)
        {
            return Reject(StatusCodes.Status400BadRequest, "noFile", NoFileMessage);
        }

        IFormFile? file;
        var limited = new LimitedReadStream(http.Request.Body, MaxBodyBytes);
        http.Request.Body = limited;
        http.Features.Set<IFormFeature>(new FormFeature(
            http.Request, new FormOptions { MultipartBodyLengthLimit = MaxBodyBytes }));
        try
        {
            var form = await http.Request.ReadFormAsync(ct);
            file = form.Files.Count == 1 ? form.Files.GetFile(UploadField) : null;
        }
        catch (Exception ex) when (limited.Exceeded
            || ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
        {
            return Reject(StatusCodes.Status413PayloadTooLarge, "tooLarge", TooLargeMessage);
        }
        catch (InvalidDataException)
        {
            return Reject(StatusCodes.Status400BadRequest, "noFile", NoFileMessage);
        }

        if (file is null || file.Length == 0)
        {
            return Reject(StatusCodes.Status400BadRequest, "noFile", NoFileMessage);
        }

        MediaImportResult result;
        await using (var stream = file.OpenReadStream())
        {
            result = await media.ImportAsync(stream, file.FileName, ct: ct);
        }

        if (result.Error is { } error)
        {
            var (status, code, message) = Describe(error);
            if (error == MediaImportError.Busy)
            {
                http.Response.Headers.RetryAfter = "5";
            }

            return Reject(status, code, message);
        }

        var asset = result.Asset!;
        audit.Record(
            "media-uploaded",
            userName,
            ip,
            $"id={asset.Id} existed={(result.AlreadyExisted ? "true" : "false")} size={asset.Width}x{asset.Height}");
        return Json(StatusCodes.Status200OK, ToResponse(asset, result.AlreadyExisted));
    }

    private static (int Status, string Code, string Message) Describe(MediaImportError error) => error switch
    {
        MediaImportError.TooLarge => (StatusCodes.Status413PayloadTooLarge, "tooLarge", TooLargeMessage),
        MediaImportError.NotAnImage => (
            StatusCodes.Status400BadRequest, "notAnImage", "The file is not an image that can be read."),
        MediaImportError.UnsupportedFormat => (
            StatusCodes.Status400BadRequest, "unsupportedFormat", "Only JPEG, PNG and WebP images are supported."),
        MediaImportError.Heic => (
            StatusCodes.Status400BadRequest,
            "heic",
            "HEIC photos are not supported. Pick the photo as JPEG (on iPhone: Settings > Camera > Formats > Most Compatible)."),
        MediaImportError.TooManyPixels => (
            StatusCodes.Status400BadRequest,
            "tooManyPixels",
            $"The image is too large. The maximum is {MediaLimits.MaxPixels / 1_000_000} megapixels."),
        MediaImportError.Busy => (
            StatusCodes.Status503ServiceUnavailable,
            "busy",
            "The server is busy processing another image. Try again in a few seconds."),
        _ => throw new InvalidOperationException($"Unsupported media import error '{error}'.")
    };

    private static async Task<IResult> ListAsync(string? page, IMediaAssetRepository assets, CancellationToken ct)
    {
        var number = 1;
        if (page is not null && (!int.TryParse(page, out number) || number < 1 || number > MaxPage))
        {
            return Error(StatusCodes.Status400BadRequest, "invalidPage", "page must be a whole number of 1 or more.");
        }

        var rows = await assets.ListNewestFirstAsync((number - 1) * PageSize, PageSize + 1, ct);
        return Json(
            StatusCodes.Status200OK,
            new MediaListResponse([.. rows.Take(PageSize).Select(a => ToResponse(a, null))], rows.Count > PageSize));
    }

    private static async Task<IResult> GetAsync(Guid id, IMediaAssetRepository assets, CancellationToken ct)
    {
        var asset = await assets.GetByIdAsync(id, ct);
        return asset is null || asset.Source != MediaSource.Upload
            ? Error(StatusCodes.Status404NotFound, "notFound", "Image not found.")
            : Json(StatusCodes.Status200OK, ToResponse(asset, null));
    }

    private static MediaAssetResponse ToResponse(MediaAsset asset, bool? alreadyExisted)
    {
        var widths = asset.GetWidths().Order().ToList();
        var preview = widths.Contains(PreviewWidth) ? PreviewWidth : widths[^1];
        return new MediaAssetResponse(
            asset.Id,
            asset.Width,
            asset.Height,
            asset.OriginalFileName,
            asset.CreatedAt,
            MediaFileNames.PublicUrl(asset, preview),
            MediaFileNames.PublicUrl(asset, widths[0]),
            [.. widths.Select(w => new MediaSourceResponse(w, MediaFileNames.PublicUrl(asset, w)))],
            alreadyExisted);
    }

    private static IResult Error(int status, string code, string message) =>
        Json(status, new MediaErrorResponse(code, [message]));

    private static IResult Json<T>(int status, T value) => Results.Json(value, JsonOptions, statusCode: status);

    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long _total;

        public bool Exceeded { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            _total += read;
            if (_total > limit)
            {
                Exceeded = true;
                throw new InvalidDataException("Request body is too large.");
            }

            return read;
        }
    }
}
