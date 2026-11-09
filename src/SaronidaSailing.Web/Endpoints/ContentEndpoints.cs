using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Identity;

namespace SaronidaSailing.Web.Endpoints;

public static class ContentEndpoints
{
    public const int MaxBodyBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/api/content")
            .RequireAuthorization(AdminPolicy.Name)
            .AddEndpointFilter(NoStore);

        group.MapPost("", SaveAsync);
        group.MapGet("history", GetHistoryAsync);
        group.MapGet("saves/{saveId:guid}", GetSaveAsync);

        return app;
    }

    private static ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return next(context);
    }

    private static async Task<IResult> SaveAsync(
        HttpContext http, IAntiforgery antiforgery, IContentService content, CancellationToken ct)
    {
        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return TooLarge();
        }

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Error(StatusCodes.Status400BadRequest, "Invalid antiforgery token.");
        }

        var body = await ReadBodyAsync(http, ct);
        if (body is null)
        {
            return TooLarge();
        }

        SaveContentRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SaveContentRequest>(body, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var path = (ex as JsonException)?.Path;
            return Error(
                StatusCodes.Status400BadRequest,
                string.IsNullOrEmpty(path) ? "Malformed request body." : $"Malformed request body at {path}.");
        }

        if (request is null || request.Changes.Any(c => c is null))
        {
            return Error(StatusCodes.Status400BadRequest, "Malformed request body.");
        }

        var editor = new ContentEditor(http.User.Identity?.Name ?? "", http.Connection.RemoteIpAddress?.ToString());
        var result = await content.ApplyChangesAsync([.. request.Changes.Select(c => ToChange(c!))], editor, ct);

        return result.Succeeded
            ? Json(StatusCodes.Status200OK, new SaveContentResponse(result.SaveId))
            : Json(StatusCodes.Status400BadRequest, new ErrorsResponse(result.Errors));
    }

    private static async Task<IResult> GetHistoryAsync(
        string? key, string? locale, string? listItemId, IContentService content, CancellationToken ct)
    {
        Guid? itemId = null;
        if (!string.IsNullOrEmpty(listItemId))
        {
            if (!Guid.TryParse(listItemId, out var parsed))
            {
                return Error(StatusCodes.Status400BadRequest, "Invalid listItemId.");
            }

            itemId = parsed;
        }

        try
        {
            var versions = await content.GetFieldHistoryAsync(key ?? "", locale ?? "", itemId, ct);
            return Json(
                StatusCodes.Status200OK,
                versions.Select(v => new FieldVersionResponse(v.SaveId, v.Value, v.ChangedBy, v.CreatedAt)));
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    private static async Task<IResult> GetSaveAsync(Guid saveId, IContentService content, CancellationToken ct)
    {
        var details = await content.GetSaveAsync(saveId, ct);
        if (details is null)
        {
            return Error(StatusCodes.Status404NotFound, "Save not found.");
        }

        return Json(
            StatusCodes.Status200OK,
            new ContentSaveResponse(
                details.SaveId,
                details.ChangedBy,
                details.CreatedAt,
                [.. details.Fields.Select(f => new SavedFieldResponse(
                    f.Key, f.Locale, f.Type, f.ListItemId, f.Value, f.PreviousValue, f.CurrentValue))]));
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        try
        {
            int read;
            while ((read = await http.Request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }

        return buffer.ToArray();
    }

    private static ContentChange ToChange(ContentChangeRequest request) => request switch
    {
        SetFieldRequest r => new SetField(r.Key, r.Locale, r.Type, r.Value, r.ListItemId),
        AddListItemRequest r => new AddListItem(r.ListKey, r.Id),
        DeleteListItemRequest r => new DeleteListItem(r.ListKey, r.Id),
        ReorderListRequest r => new ReorderList(r.ListKey, r.OrderedIds),
        SetListItemActiveRequest r => new SetListItemActive(r.ListKey, r.Id, r.IsActive),
        SetSectionActiveRequest r => new SetSectionActive(r.SectionKey, r.IsActive),
        _ => throw new InvalidOperationException($"Unsupported change request '{request.GetType().Name}'.")
    };

    private static IResult TooLarge() =>
        Error(StatusCodes.Status413PayloadTooLarge, "Request body is too large.");

    private static IResult Error(int status, string message) => Json(status, new ErrorsResponse([message]));

    private static IResult Json<T>(int status, T value) => Results.Json(value, JsonOptions, statusCode: status);
}
