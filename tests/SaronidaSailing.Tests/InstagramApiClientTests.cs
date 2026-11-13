using System.Net;
using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Instagram;

namespace SaronidaSailing.Tests;

public sealed class InstagramApiClientTests
{
    private const string AppId = "app-1";
    private const string AppSecret = "SECRET-xyz";
    private const string Token = "TOKEN-abc";
    private const string Redirect = "https://example.test/admin/instagram/callback";

    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildAuthorizeUrl_EncodesEveryValue()
    {
        var (client, _) = NewClient();

        var url = client.BuildAuthorizeUrl("https://example.test/admin/instagram/callback?a=b", "s t/ate+");

        Assert.Equal(
            "https://www.instagram.com/oauth/authorize?client_id=app-1"
            + "&redirect_uri=https%3A%2F%2Fexample.test%2Fadmin%2Finstagram%2Fcallback%3Fa%3Db"
            + "&response_type=code&scope=instagram_business_basic&state=s%20t%2Fate%2B",
            url);
    }

    [Fact]
    public void BuildAuthorizeUrl_WithoutConfiguration_Throws()
    {
        var (client, _) = NewClient(appId: "", appSecret: "");

        Assert.Throws<InvalidOperationException>(() => client.BuildAuthorizeUrl(Redirect, "state"));
    }

    [Theory]
    [InlineData("""{ "access_token": "SHORT", "user_id": 17841400000000001 }""")]
    [InlineData("""{ "access_token": "SHORT", "user_id": "17841400000000001" }""")]
    [InlineData("""{ "data": [ { "access_token": "SHORT", "user_id": 17841400000000001, "permissions": "instagram_business_basic" } ] }""")]
    public async Task ExchangeCode_AcceptsFlatAndDataShapes(string json)
    {
        var (client, handler) = NewClient(_ => Json(json));

        var token = await client.ExchangeCodeAsync("the-code", Redirect);

        Assert.Equal("SHORT", token.AccessToken);
        Assert.Equal("17841400000000001", token.UserId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.instagram.com/oauth/access_token", request.Uri.ToString());
        var form = HttpUtility.ParseQueryString(request.Body);
        Assert.Equal(AppId, form["client_id"]);
        Assert.Equal(AppSecret, form["client_secret"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(Redirect, form["redirect_uri"]);
        Assert.Equal("the-code", form["code"]);
    }

    [Fact]
    public async Task ExchangeCode_StripsTrailingHashUnderscore()
    {
        var (client, handler) = NewClient(_ => Json("""{ "access_token": "SHORT", "user_id": 1 }"""));

        await client.ExchangeCodeAsync("AQD-code#_", Redirect);

        Assert.Equal("AQD-code", HttpUtility.ParseQueryString(handler.Requests[0].Body)["code"]);
    }

    [Fact]
    public async Task ExchangeCode_WithoutAccessToken_IsOther()
    {
        var (client, _) = NewClient(_ => Json("{}"));

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.ExchangeCodeAsync("c", Redirect));

        Assert.Equal(InstagramApiErrorKind.Other, ex.Kind);
    }

    [Fact]
    public async Task ExchangeForLongLived_SendsTheDocumentedRequest_AndComputesExpiry()
    {
        var (client, handler) = NewClient(_ =>
            Json("""{ "access_token": "LONG", "token_type": "bearer", "expires_in": 5184000 }"""));

        var token = await client.ExchangeForLongLivedAsync(Token);

        Assert.Equal("LONG", token.AccessToken);
        Assert.Equal(Now.UtcDateTime.AddSeconds(5184000), token.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, token.ExpiresAt.Kind);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("graph.instagram.com", request.Uri.Host);
        Assert.Equal("/access_token", request.Uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal("ig_exchange_token", query["grant_type"]);
        Assert.Equal(AppSecret, query["client_secret"]);
        Assert.Equal(Token, query["access_token"]);
    }

    [Fact]
    public async Task Refresh_SendsTheDocumentedRequest_AndComputesExpiry()
    {
        var (client, handler) = NewClient(_ =>
            Json("""{ "access_token": "REFRESHED", "token_type": "bearer", "expires_in": 3600 }"""));

        var token = await client.RefreshAsync(Token);

        Assert.Equal("REFRESHED", token.AccessToken);
        Assert.Equal(Now.UtcDateTime.AddHours(1), token.ExpiresAt);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/refresh_access_token", request.Uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal("ig_refresh_token", query["grant_type"]);
        Assert.Equal(Token, query["access_token"]);
        Assert.Null(query["client_secret"]);
    }

    [Fact]
    public async Task GetProfile_ReturnsIdAndUsername()
    {
        var (client, handler) = NewClient(_ => Json("""{ "user_id": "17841400000000001", "username": "saronida.sailing", "id": "999" }"""));

        var profile = await client.GetProfileAsync(Token);

        Assert.Equal(new InstagramProfile("17841400000000001", "saronida.sailing"), profile);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/me", request.Uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal("user_id,username", query["fields"]);
        Assert.Equal(Token, query["access_token"]);
    }

    [Fact]
    public async Task GetProfile_FallsBackToId()
    {
        var (client, _) = NewClient(_ => Json("""{ "id": 77, "username": "u" }"""));

        Assert.Equal(new InstagramProfile("77", "u"), await client.GetProfileAsync(Token));
    }

    [Fact]
    public async Task GetRecentMedia_RequestsOnePageWithTheFieldsAndLimit()
    {
        var (client, handler) = NewClient(_ => Json("""{ "data": [] }"""));

        var items = await client.GetRecentMediaAsync(Token, 12);

        Assert.Empty(items);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/me/media", request.Uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal(
            "id,caption,media_type,media_product_type,media_url,thumbnail_url,permalink,timestamp,children{media_type,media_url,thumbnail_url}",
            query["fields"]);
        Assert.Equal("12", query["limit"]);
        Assert.Equal(Token, query["access_token"]);
    }

    [Fact]
    public async Task GetRecentMedia_MapsEveryMediaKind_AndSkipsItemsWithoutAnImage()
    {
        const string json = """
            { "data": [
              { "id": "1", "caption": "A photo", "media_type": "IMAGE", "media_product_type": "FEED",
                "media_url": "https://cdn.test/1.jpg", "permalink": "https://www.instagram.com/p/1/", "timestamp": "2026-11-10T12:30:00+0000" },
              { "id": "2", "media_type": "VIDEO", "media_product_type": "FEED",
                "media_url": "https://cdn.test/2.mp4", "thumbnail_url": "https://cdn.test/2-thumb.jpg",
                "permalink": "https://www.instagram.com/p/2/", "timestamp": "2026-11-09T08:00:00+0000" },
              { "id": "3", "caption": "A reel", "media_type": "VIDEO", "media_product_type": "REELS",
                "media_url": "https://cdn.test/3.mp4", "thumbnail_url": "https://cdn.test/3-thumb.jpg",
                "permalink": "https://www.instagram.com/reel/3/", "timestamp": "2026-11-08T08:00:00+0000" },
              { "id": "4", "media_type": "CAROUSEL_ALBUM", "media_url": "https://cdn.test/4-album.jpg",
                "permalink": "https://www.instagram.com/p/4/", "timestamp": "2026-11-07T08:00:00+0000",
                "children": { "data": [
                  { "media_type": "IMAGE", "media_url": "https://cdn.test/4-a.jpg" },
                  { "media_type": "VIDEO", "media_url": "https://cdn.test/4-b.mp4", "thumbnail_url": "https://cdn.test/4-b-thumb.jpg" } ] } },
              { "id": "5", "media_type": "CAROUSEL_ALBUM", "media_url": "https://cdn.test/5-album.jpg",
                "permalink": "https://www.instagram.com/p/5/", "timestamp": "2026-11-06T08:00:00+0000",
                "children": { "data": [
                  { "media_type": "VIDEO", "media_url": "https://cdn.test/5-a.mp4", "thumbnail_url": "https://cdn.test/5-a-thumb.jpg" },
                  { "media_type": "IMAGE", "media_url": "https://cdn.test/5-b.jpg" } ] } },
              { "id": "6", "media_type": "CAROUSEL_ALBUM", "media_url": "https://cdn.test/6-album.jpg",
                "permalink": "https://www.instagram.com/p/6/", "timestamp": "2026-11-05T08:00:00+0000" },
              { "id": "7", "media_type": "VIDEO", "media_url": "https://cdn.test/7.mp4",
                "permalink": "https://www.instagram.com/p/7/", "timestamp": "2026-11-04T08:00:00+0000" },
              { "id": "8", "media_type": "IMAGE",
                "permalink": "https://www.instagram.com/p/8/", "timestamp": "2026-11-03T08:00:00+0000" }
            ] }
            """;
        var logger = new CapturingLogger();
        var (client, _) = NewClient(_ => Json(json), logger: logger);

        var items = await client.GetRecentMediaAsync(Token, 12);

        Assert.Equal(["1", "2", "3", "4", "5", "6"], items.Select(i => i.Id));
        Assert.Equal(
            [
                "https://cdn.test/1.jpg",
                "https://cdn.test/2-thumb.jpg",
                "https://cdn.test/3-thumb.jpg",
                "https://cdn.test/4-a.jpg",
                "https://cdn.test/5-a-thumb.jpg",
                "https://cdn.test/6-album.jpg",
            ],
            items.Select(i => i.ImageUrl));
        Assert.Equal(["IMAGE", "VIDEO", "REELS", "CAROUSEL_ALBUM", "CAROUSEL_ALBUM", "CAROUSEL_ALBUM"], items.Select(i => i.MediaType));
        Assert.Equal("A photo", items[0].Caption);
        Assert.Null(items[1].Caption);
        Assert.Equal("https://www.instagram.com/p/1/", items[0].Permalink);
        Assert.Equal(new DateTime(2026, 11, 10, 12, 30, 0, DateTimeKind.Utc), items[0].PostedAt);
        Assert.Equal(DateTimeKind.Utc, items[0].PostedAt.Kind);

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("7") && !w.Contains("http"));
        Assert.Contains(warnings, w => w.Contains("8") && !w.Contains("http"));
    }

    [Theory]
    [InlineData(400, """{ "error": { "message": "Invalid OAuth access token.", "type": "OAuthException", "code": 190 } }""", InstagramApiErrorKind.TokenInvalid)]
    [InlineData(500, "Internal error", InstagramApiErrorKind.Transient)]
    [InlineData(503, """{ "error": { "message": "down", "code": 2 } }""", InstagramApiErrorKind.Transient)]
    [InlineData(400, """{ "error": { "message": "Application request limit reached", "type": "OAuthException", "code": 4 } }""", InstagramApiErrorKind.Transient)]
    [InlineData(400, """{ "error": { "message": "x", "code": 1 } }""", InstagramApiErrorKind.Transient)]
    [InlineData(400, """{ "error": { "message": "x", "code": 17 } }""", InstagramApiErrorKind.Transient)]
    [InlineData(400, """{ "error": { "message": "x", "code": 341 } }""", InstagramApiErrorKind.Transient)]
    [InlineData(400, """{ "error": { "message": "Unsupported get request.", "type": "GraphMethodException", "code": 100 } }""", InstagramApiErrorKind.Other)]
    [InlineData(400, """{ "error_type": "OAuthException", "code": 400, "error_message": "Invalid authorization code" }""", InstagramApiErrorKind.Other)]
    [InlineData(404, "not json", InstagramApiErrorKind.Other)]
    public async Task ErrorResponses_AreClassified(int status, string body, InstagramApiErrorKind expected)
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.GetProfileAsync(Token));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task ErrorMessage_CarriesMetasCodeAndMessage()
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{ "error": { "message": "Unsupported get request.", "code": 100 } }"""),
        });

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.GetProfileAsync(Token));

        Assert.Equal(100, ex.ErrorCode);
        Assert.Contains("100", ex.Message);
        Assert.Contains("Unsupported get request.", ex.Message);
    }

    [Fact]
    public async Task ErrorMessage_NeverContainsTheTokenTheSecretOrTheUrl()
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                $$"""{ "error": { "message": "Bad {{Token}} and {{AppSecret}} at https://graph.instagram.com/x?access_token={{Token}}", "code": 190 } }"""),
        });

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.ExchangeForLongLivedAsync(Token));

        Assert.DoesNotContain(Token, ex.Message);
        Assert.DoesNotContain(AppSecret, ex.Message);
        Assert.DoesNotContain("access_token=", ex.Message);
        Assert.DoesNotContain("client_secret", ex.Message);
    }

    [Fact]
    public async Task NetworkError_IsTransient_AndKeepsNoUrl()
    {
        var (client, _) = NewClient(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.GetProfileAsync(Token));

        Assert.Equal(InstagramApiErrorKind.Transient, ex.Kind);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Fact]
    public async Task Timeout_IsTransient()
    {
        var (client, _) = NewClient(_ => throw new TaskCanceledException("timeout", new TimeoutException()));

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.GetProfileAsync(Token));

        Assert.Equal(InstagramApiErrorKind.Transient, ex.Kind);
    }

    [Fact]
    public async Task CallerCancellation_IsNotWrapped()
    {
        var (client, _) = NewClient(_ => Json("{}"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetProfileAsync(Token, cts.Token));
    }

    [Fact]
    public async Task DownloadImage_ReturnsTheBytes()
    {
        var (client, handler) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });

        await using var stream = await client.DownloadImageAsync("https://cdn.test/a.jpg");

        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal([1, 2, 3, 4], copy.ToArray());
        Assert.Equal("https://cdn.test/a.jpg", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task DownloadImage_DeclaredOverTheLimit_IsRejected()
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[MediaLimits.MaxUploadBytes + 1]),
        });

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.DownloadImageAsync("https://cdn.test/a.jpg"));

        Assert.Equal(InstagramApiErrorKind.Other, ex.Kind);
    }

    [Fact]
    public async Task DownloadImage_StreamedOverTheLimitWithoutLength_IsRejected()
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[MediaLimits.MaxUploadBytes + 1]),
        });

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.DownloadImageAsync("https://cdn.test/a.jpg"));

        Assert.Equal(InstagramApiErrorKind.Other, ex.Kind);
    }

    [Fact]
    public async Task DownloadImage_ExactlyTheLimit_IsAccepted()
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[MediaLimits.MaxUploadBytes]),
        });

        await using var stream = await client.DownloadImageAsync("https://cdn.test/a.jpg");

        Assert.Equal(MediaLimits.MaxUploadBytes, stream.Length);
    }

    [Theory]
    [InlineData("http://cdn.test/a.jpg")]
    [InlineData("ftp://cdn.test/a.jpg")]
    [InlineData("/relative/a.jpg")]
    [InlineData("not a url")]
    public async Task DownloadImage_NonHttps_IsRejectedWithoutARequest(string url)
    {
        var (client, handler) = NewClient(_ => Json("{}"));

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.DownloadImageAsync(url));

        Assert.Equal(InstagramApiErrorKind.Other, ex.Kind);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(404, InstagramApiErrorKind.Other)]
    [InlineData(502, InstagramApiErrorKind.Transient)]
    public async Task DownloadImage_HttpError_IsClassified(int status, InstagramApiErrorKind expected)
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage((HttpStatusCode)status));

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.DownloadImageAsync("https://cdn.test/a.jpg"));

        Assert.Equal(expected, ex.Kind);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (InstagramApiClient Client, StubHandler Handler) NewClient(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null,
        string appId = AppId,
        string appSecret = AppSecret,
        ILogger<InstagramApiClient>? logger = null)
    {
        var handler = new StubHandler(respond ?? (_ => Json("{}")));
        var options = Options.Create(new InstagramOptions { AppId = appId, AppSecret = appSecret });
        var client = new InstagramApiClient(
            new HttpClient(handler),
            options,
            new FakeTimeProvider(Now),
            logger ?? NullLogger<InstagramApiClient>.Instance);
        return (client, handler);
    }

    internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body);

    internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
            return respond(request);
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    internal sealed record LogEntry(LogLevel Level, string Message);

    internal sealed class CapturingLogger : ILogger<InstagramApiClient>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
