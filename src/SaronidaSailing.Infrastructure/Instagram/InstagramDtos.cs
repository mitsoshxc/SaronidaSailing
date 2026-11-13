using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaronidaSailing.Infrastructure.Instagram;

internal sealed class TokenResponseDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("user_id")]
    public JsonElement? UserId { get; set; }

    [JsonPropertyName("expires_in")]
    public long? ExpiresIn { get; set; }

    [JsonPropertyName("data")]
    public List<TokenResponseDto>? Data { get; set; }
}

internal sealed class ProfileDto
{
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("user_id")]
    public JsonElement? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

internal sealed class MediaListDto
{
    [JsonPropertyName("data")]
    public List<MediaDto>? Data { get; set; }
}

internal sealed class MediaDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("media_product_type")]
    public string? MediaProductType { get; set; }

    [JsonPropertyName("media_url")]
    public string? MediaUrl { get; set; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("permalink")]
    public string? Permalink { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("children")]
    public ChildrenDto? Children { get; set; }
}

internal sealed class ChildrenDto
{
    [JsonPropertyName("data")]
    public List<ChildDto>? Data { get; set; }
}

internal sealed class ChildDto
{
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("media_url")]
    public string? MediaUrl { get; set; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }
}
