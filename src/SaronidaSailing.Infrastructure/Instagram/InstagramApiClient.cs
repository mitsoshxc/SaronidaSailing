using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Infrastructure.Instagram;

public sealed partial class InstagramApiClient(
    HttpClient http,
    IOptions<InstagramOptions> options,
    TimeProvider time,
    ILogger<InstagramApiClient> logger) : IInstagramApiClient
{
    private const string Scope = "instagram_business_basic";
    private const string MediaFields =
        "id,caption,media_type,media_product_type,media_url,thumbnail_url,permalink,timestamp,children{media_type,media_url,thumbnail_url}";
    private const int MaxMessageLength = 300;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly int[] TransientCodes = [1, 2, 4, 17, 341];

    private InstagramOptions Options => options.Value;

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    public string BuildAuthorizeUrl(string redirectUri, string state)
    {
        RequireConfigured();
        var separator = Options.AuthorizeUrl.Contains('?') ? "&" : "?";
        return Options.AuthorizeUrl + separator + Query(
            ("client_id", Options.AppId),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("scope", Scope),
            ("state", state));
    }

    public async Task<InstagramShortLivedToken> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        RequireConfigured();
        var cleanCode = code.Trim();
        if (cleanCode.EndsWith("#_", StringComparison.Ordinal))
        {
            cleanCode = cleanCode[..^2];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Url(Options.ApiBaseUrl, "oauth/access_token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Options.AppId,
                ["client_secret"] = Options.AppSecret,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
                ["code"] = cleanCode,
            }),
        };

        var body = await SendAsync(request, [Options.AppSecret, cleanCode], ct);
        var dto = Parse<TokenResponseDto>(body);
        var token = dto.Data is { Count: > 0 } data ? data[0] : dto;
        var userId = AsString(token.UserId);
        if (string.IsNullOrWhiteSpace(token.AccessToken) || userId is null)
        {
            throw Unreadable();
        }

        return new InstagramShortLivedToken(token.AccessToken, userId);
    }

    public async Task<InstagramLongLivedToken> ExchangeForLongLivedAsync(string shortLivedToken, CancellationToken ct = default)
    {
        RequireConfigured();
        var url = Url(Options.GraphBaseUrl, "access_token", Query(
            ("grant_type", "ig_exchange_token"),
            ("client_secret", Options.AppSecret),
            ("access_token", shortLivedToken)));
        return await GetLongLivedAsync(url, [Options.AppSecret, shortLivedToken], ct);
    }

    public async Task<InstagramLongLivedToken> RefreshAsync(string longLivedToken, CancellationToken ct = default)
    {
        var url = Url(Options.GraphBaseUrl, "refresh_access_token", Query(
            ("grant_type", "ig_refresh_token"),
            ("access_token", longLivedToken)));
        return await GetLongLivedAsync(url, [longLivedToken], ct);
    }

    public async Task<InstagramProfile> GetProfileAsync(string token, CancellationToken ct = default)
    {
        var url = Url(Options.GraphBaseUrl, "me", Query(("fields", "user_id,username"), ("access_token", token)));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var dto = Parse<ProfileDto>(await SendAsync(request, [token], ct));

        var id = AsString(dto.UserId) ?? AsString(dto.Id);
        if (id is null || string.IsNullOrWhiteSpace(dto.Username))
        {
            throw Unreadable();
        }

        return new InstagramProfile(id, dto.Username);
    }

    public async Task<IReadOnlyList<InstagramMediaItem>> GetRecentMediaAsync(string token, int limit, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var url = Url(Options.GraphBaseUrl, "me/media", Query(
            ("fields", MediaFields),
            ("limit", limit.ToString(CultureInfo.InvariantCulture)),
            ("access_token", token)));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var list = Parse<MediaListDto>(await SendAsync(request, [token], ct));

        var items = new List<InstagramMediaItem>();
        foreach (var dto in list.Data ?? [])
        {
            if (ToItem(dto) is { } item)
            {
                items.Add(item);
            }
            else
            {
                logger.LogWarning("Skipping Instagram post {PostId}: no usable image or a required field is missing.", dto.Id);
            }
        }

        return items;
    }

    public async Task<Stream> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InstagramApiException(InstagramApiErrorKind.Other, "Image URL must be an absolute https URL.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode >= 500 ? InstagramApiErrorKind.Transient : InstagramApiErrorKind.Other;
                throw new InstagramApiException(kind, $"Image download failed (HTTP {(int)response.StatusCode}).");
            }

            if (response.Content.Headers.ContentLength > MediaLimits.MaxUploadBytes)
            {
                throw TooLarge();
            }

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MediaLimits.MaxUploadBytes)
                {
                    throw TooLarge();
                }

                buffer.Write(chunk, 0, read);
            }

            buffer.Position = 0;
            return buffer;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InstagramApiException(InstagramApiErrorKind.Transient, "Image download timed out.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new InstagramApiException(InstagramApiErrorKind.Transient, "Image download failed (network error).", innerException: ex);
        }
    }

    private async Task<InstagramLongLivedToken> GetLongLivedAsync(string url, string[] secrets, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var dto = Parse<TokenResponseDto>(await SendAsync(request, secrets, ct));
        if (string.IsNullOrWhiteSpace(dto.AccessToken) || dto.ExpiresIn is not { } expiresIn)
        {
            throw Unreadable();
        }

        return new InstagramLongLivedToken(dto.AccessToken, time.GetUtcNow().UtcDateTime.AddSeconds(expiresIn));
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string[] secrets, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            throw ToException(response.StatusCode, body, secrets);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InstagramApiException(InstagramApiErrorKind.Transient, "Instagram API request timed out.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new InstagramApiException(InstagramApiErrorKind.Transient, "Instagram API request failed (network error).", innerException: ex);
        }
    }

    private static InstagramApiException ToException(HttpStatusCode status, string body, string[] secrets)
    {
        int? code = null;
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var source = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? error : root;
                code = ReadInt(source, "code");
                message = ReadString(source, "message") ?? ReadString(source, "error_message");
            }
        }
        catch (JsonException)
        {
        }

        var kind = code switch
        {
            190 => InstagramApiErrorKind.TokenInvalid,
            { } c when TransientCodes.Contains(c) => InstagramApiErrorKind.Transient,
            _ => (int)status >= 500 ? InstagramApiErrorKind.Transient : InstagramApiErrorKind.Other,
        };

        var text = $"Instagram API error (HTTP {(int)status}" + (code is { } value ? $", code {value}" : "") + ")";
        if (!string.IsNullOrWhiteSpace(message))
        {
            text += ": " + Clean(message, secrets);
        }

        return new InstagramApiException(kind, text, code);
    }

    private static string Clean(string message, string[] secrets)
    {
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
        {
            message = message.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        message = UrlPattern().Replace(message, "[url]").ReplaceLineEndings(" ");
        return message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
    }

    private static InstagramMediaItem? ToItem(MediaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id)
            || string.IsNullOrWhiteSpace(dto.Permalink)
            || string.IsNullOrWhiteSpace(dto.MediaType)
            || !DateTimeOffset.TryParse(dto.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var postedAt))
        {
            return null;
        }

        var imageUrl = dto.MediaType switch
        {
            "IMAGE" => dto.MediaUrl,
            "VIDEO" => dto.ThumbnailUrl,
            "CAROUSEL_ALBUM" => CarouselImageUrl(dto),
            _ => dto.MediaUrl ?? dto.ThumbnailUrl,
        };
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var mediaType = dto.MediaProductType == "REELS" ? "REELS" : dto.MediaType;
        return new InstagramMediaItem(dto.Id, dto.Caption, mediaType, dto.Permalink, postedAt.UtcDateTime, imageUrl);
    }

    private static string? CarouselImageUrl(MediaDto dto)
    {
        var first = dto.Children?.Data?.FirstOrDefault();
        var fromChild = first is null ? null : first.MediaType == "IMAGE" ? first.MediaUrl : first.ThumbnailUrl;
        return string.IsNullOrWhiteSpace(fromChild) ? dto.MediaUrl : fromChild;
    }

    private void RequireConfigured()
    {
        if (!Options.IsConfigured)
        {
            throw new InvalidOperationException("Instagram:AppId and Instagram:AppSecret are not configured.");
        }
    }

    private static string Url(string baseUrl, string path, string? query = null) =>
        $"{baseUrl.TrimEnd('/')}/{path}" + (query is null ? "" : "?" + query);

    private static string Query(params (string Name, string Value)[] parameters) =>
        string.Join('&', parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));

    private static T Parse<T>(string body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions) ?? throw Unreadable();
        }
        catch (JsonException ex)
        {
            throw new InstagramApiException(InstagramApiErrorKind.Other, "Instagram API returned an unreadable response.", innerException: ex);
        }
    }

    private static InstagramApiException Unreadable() =>
        new(InstagramApiErrorKind.Other, "Instagram API returned an unreadable response.");

    private static InstagramApiException TooLarge() =>
        new(InstagramApiErrorKind.Other, $"Image is larger than {MediaLimits.MaxUploadBytes / (1024 * 1024)} MB.");

    private static string? AsString(JsonElement? element) => element?.ValueKind switch
    {
        JsonValueKind.String => element.Value.GetString(),
        JsonValueKind.Number => element.Value.GetRawText(),
        _ => null,
    };

    private static int? ReadInt(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? ReadString(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
