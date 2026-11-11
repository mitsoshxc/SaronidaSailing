using System.Text.Json.Serialization;

namespace SaronidaSailing.Web.Endpoints;

public sealed record MediaSourceResponse(int Width, string Url);

public sealed record MediaAssetResponse(
    Guid Id,
    int Width,
    int Height,
    string OriginalFileName,
    DateTime CreatedAt,
    string PreviewUrl,
    string ThumbUrl,
    IReadOnlyList<MediaSourceResponse> Sources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AlreadyExisted = null);

public sealed record MediaListResponse(IReadOnlyList<MediaAssetResponse> Items, bool HasMore);

public sealed record MediaErrorResponse(string Code, IReadOnlyList<string> Errors);
