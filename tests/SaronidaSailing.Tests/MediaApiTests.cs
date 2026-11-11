using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Media;
using SaronidaSailing.Web.Endpoints;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

public sealed class MediaApiWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Media:ProcessingWaitSeconds", "0");
    }
}

[Collection(WebHostCollection.Name)]
public class MediaApiTests(MediaApiWebApplicationFactory factory) : IClassFixture<MediaApiWebApplicationFactory>
{
    private const string ApiPath = "/admin/api/media";
    private const string ContentPath = "/admin/api/content";

    private static int _seed = 100;

    [Fact]
    public async Task Anonymous_GetsUnauthorized()
    {
        var client = NewClient(factory);

        var post = await client.PostAsync(ApiPath, Multipart("file", "a.jpg", NextPhoto(200, 100)));
        var list = await client.GetAsync(ApiPath);
        var one = await client.GetAsync($"{ApiPath}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, one.StatusCode);
        Assert.Null(post.Headers.Location);
    }

    [Fact]
    public async Task MissingAntiforgeryToken_IsRejected()
    {
        var (client, _) = await SignInAsync("media-csrf@example.com");

        var missing = await client.PostAsync(ApiPath, Multipart("file", "a.jpg", NextPhoto(200, 100)));
        var wrong = await UploadAsync(client, "not-a-token", NextPhoto(200, 100));

        foreach (var response in new[] { missing, wrong })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalidAntiforgery", (await ReadErrorAsync(response)).Code);
        }
    }

    [Fact]
    public async Task Upload_Returns200WithUrlsAndWritesTheFiles()
    {
        const string email = "media-upload@example.com";
        var (client, token) = await SignInAsync(email);
        var bytes = MediaTestImages.Photo(3000, 2000);

        var response = await UploadAsync(client, token, bytes, "photo.jpg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3000, body.GetProperty("width").GetInt32());
        Assert.Equal(2000, body.GetProperty("height").GetInt32());
        Assert.Equal("photo.jpg", body.GetProperty("originalFileName").GetString());
        Assert.False(body.GetProperty("alreadyExisted").GetBoolean());
        Assert.True(body.GetProperty("createdAt").GetDateTime() > DateTime.UtcNow.AddMinutes(-5));
        var id = body.GetProperty("id").GetGuid();

        var sources = body.GetProperty("sources").EnumerateArray()
            .Select(s => (Width: s.GetProperty("width").GetInt32(), Url: s.GetProperty("url").GetString()!))
            .ToList();
        Assert.Equal([480, 960, 1600, 2400], sources.Select(s => s.Width));
        Assert.All(sources, s => Assert.StartsWith("/media/photo-", s.Url));
        Assert.All(sources, s => Assert.EndsWith($"-{s.Width}.webp", s.Url));
        Assert.Equal(sources[1].Url, body.GetProperty("previewUrl").GetString());
        Assert.Equal(sources[0].Url, body.GetProperty("thumbUrl").GetString());
        Assert.All(sources, s => Assert.True(File.Exists(Path.Combine(factory.MediaPath, s.Url["/media/".Length..]))));
        Assert.Single(Directory.GetFiles(factory.OriginalsPath, "*.jpg"), f => new FileInfo(f).Length == bytes.Length);

        var audit = ReadLogLines(factory.LogsPath, "audit").Single(l => l.Contains($"id={id}"));
        Assert.Contains("|media-uploaded|", audit);
        Assert.Contains($"user={email}", audit);
        Assert.Contains("existed=false size=3000x2000", audit);
        Assert.DoesNotContain("photo.jpg", audit);
    }

    [Fact]
    public async Task SameFileTwice_ReturnsTheSameAssetAndSaysSo()
    {
        const string email = "media-twice@example.com";
        var (client, token) = await SignInAsync(email);
        var bytes = NextPhoto(640, 480);

        var first = await (await UploadAsync(client, token, bytes, "one.jpg")).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await UploadAsync(client, token, bytes, "two.jpg")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(first.GetProperty("alreadyExisted").GetBoolean());
        Assert.True(second.GetProperty("alreadyExisted").GetBoolean());
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal("one.jpg", second.GetProperty("originalFileName").GetString());
        var lines = ReadLogLines(factory.LogsPath, "audit")
            .Where(l => l.Contains($"id={first.GetProperty("id").GetGuid()}"))
            .ToList();
        Assert.Contains(lines, l => l.Contains("existed=false"));
        Assert.Contains(lines, l => l.Contains("existed=true"));
    }

    [Fact]
    public async Task ImageNarrowerThanTheWidths_FallsBackToTheLargestForPreview()
    {
        var (client, token) = await SignInAsync("media-narrow@example.com");

        var response = await UploadAsync(client, token, NextPhoto(300, 200));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sources = body.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("url").GetString()).ToList();
        Assert.Equal(sources[^1], body.GetProperty("previewUrl").GetString());
        Assert.Equal(sources[0], body.GetProperty("thumbUrl").GetString());
        Assert.DoesNotContain("-960.webp", body.GetProperty("previewUrl").GetString());
    }

    [Theory]
    [InlineData("notAnImage", 400)]
    [InlineData("unsupportedFormat", 400)]
    [InlineData("heic", 400)]
    [InlineData("tooManyPixels", 400)]
    public async Task ImportErrors_AreMappedToCodes(string code, int status)
    {
        var email = $"media-{code}@example.com";
        var (client, token) = await SignInAsync(email);
        byte[] bytes = code switch
        {
            "notAnImage" => "this is not a picture"u8.ToArray(),
            "unsupportedFormat" => MediaTestImages.TinyGif,
            "heic" => [0x00, 0x00, 0x00, 0x18, .. "ftypheic"u8.ToArray(), 0x00, 0x00, 0x00, 0x00, .. "mif1heic"u8.ToArray()],
            _ => MediaTestImages.PngClaiming(8000, 6300),
        };

        var response = await UploadAsync(client, token, bytes, "evil-file-name.jpg");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var error = await ReadErrorAsync(response);
        Assert.Equal(code, error.Code);
        Assert.Single(error.Errors);
        if (code == "heic")
        {
            Assert.Contains("JPEG", error.Errors[0]);
            Assert.Contains("Most Compatible", error.Errors[0]);
        }

        var audit = ReadLogLines(factory.LogsPath, "audit").Single(l => l.Contains("|media-rejected|") && l.Contains($"user={email}"));
        Assert.Contains("|media-rejected|", audit);
        Assert.EndsWith($"code={code}", audit);
        Assert.DoesNotContain("evil-file-name", audit);
    }

    [Fact]
    public async Task FileOverTheLimit_IsTooLarge()
    {
        var (client, token) = await SignInAsync("media-large-file@example.com");

        var response = await UploadAsync(client, token, new byte[MediaLimits.MaxUploadBytes + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("tooLarge", (await ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task BodyOverTheLimit_IsTooLargeWithoutBeingRead()
    {
        var (client, token) = await SignInAsync("media-large-body@example.com");

        var response = await UploadAsync(client, token, new byte[MediaEndpoints.MaxBodyBytes + 1024]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("tooLarge", (await ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task ContentLengthOverTheLimit_IsTooLargeWithoutReading()
    {
        const string email = "media-large-length@example.com";
        var (client, token) = await SignInAsync(email);
        var request = new HttpRequestMessage(HttpMethod.Post, ApiPath) { Content = new LyingLengthContent(MediaEndpoints.MaxBodyBytes + 1) };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("tooLarge", (await ReadErrorAsync(response)).Code);
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains($"user={email}") && l.EndsWith("code=tooLarge"));
    }

    [Fact]
    public async Task NoFile_IsRejected()
    {
        var (client, token) = await SignInAsync("media-nofile@example.com");
        var photo = NextPhoto(200, 100);

        var textOnly = new MultipartFormDataContent { { new StringContent("hello"), "file" } };
        var empty = Multipart("file", "empty.jpg", []);
        var wrongField = Multipart("picture", "a.jpg", photo);
        var two = Multipart("file", "a.jpg", photo);
        two.Add(new ByteArrayContent(photo), "file", "b.jpg");
        var notMultipart = new StringContent("{}", Encoding.UTF8, "application/json");

        foreach (var content in new HttpContent[] { textOnly, empty, wrongField, two, notMultipart })
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ApiPath) { Content = content };
            request.Headers.Add("RequestVerificationToken", token);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("noFile", (await ReadErrorAsync(response)).Code);
        }
    }

    [Fact]
    public async Task WhileAnotherUploadIsBeingProcessed_AnswersBusyWithRetryAfter()
    {
        var (client, token) = await SignInAsync("media-busy@example.com");
        var gate = factory.Services.GetRequiredService<MediaProcessingGate>();
        Assert.True(await gate.WaitAsync(TimeSpan.Zero));
        HttpResponseMessage response;
        try
        {
            response = await UploadAsync(client, token, NextPhoto(640, 480));
        }
        finally
        {
            gate.Release();
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("busy", (await ReadErrorAsync(response)).Code);
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task GetById_ReturnsTheAssetOr404()
    {
        var (client, token) = await SignInAsync("media-get@example.com");
        var uploaded = await (await UploadAsync(client, token, NextPhoto(640, 480))).Content.ReadFromJsonAsync<JsonElement>();
        var id = uploaded.GetProperty("id").GetGuid();

        var found = await client.GetAsync($"{ApiPath}/{id}");
        var missing = await client.GetAsync($"{ApiPath}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("no-store", found.Headers.CacheControl?.ToString());
        var body = await found.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal(uploaded.GetProperty("previewUrl").GetString(), body.GetProperty("previewUrl").GetString());
        Assert.False(body.TryGetProperty("alreadyExisted", out _));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("notFound", (await ReadErrorAsync(missing)).Code);
    }

    [Fact]
    public async Task SavingContent_WithAnImageAndAlt_Succeeds_AndWithoutAlt_Fails()
    {
        var (client, token) = await SignInAsync("media-save@example.com");
        var uploaded = await (await UploadAsync(client, token, NextPhoto(640, 480))).Content.ReadFromJsonAsync<JsonElement>();
        var id = uploaded.GetProperty("id").GetGuid();

        var withoutAlt = await SaveAsync(client, token, $$"""{"changes":[{"op":"setField","key":"mediasave.hero.image","locale":"el","type":"image","value":"{{id}}"}]}""");
        var english = await SaveAsync(client, token, $$"""{"changes":[{"op":"setField","key":"mediasave.hero.image","locale":"en","type":"image","value":"{{id}}"}]}""");
        var withAlt = await SaveAsync(
            client,
            token,
            $$"""
            {"changes":[
              {"op":"setField","key":"mediasave.hero.image","locale":"el","type":"image","value":"{{id}}"},
              {"op":"setField","key":"mediasave.hero.image-alt","locale":"el","type":"text","value":"A boat"}
            ]}
            """);

        Assert.Equal(HttpStatusCode.BadRequest, withoutAlt.StatusCode);
        Assert.Equal(["'mediasave.hero.image': Greek alt text is required."], await ReadErrorsAsync(withoutAlt));
        Assert.Equal(HttpStatusCode.BadRequest, english.StatusCode);
        Assert.Equal(["Images are stored in Greek only: 'mediasave.hero.image'."], await ReadErrorsAsync(english));
        Assert.Equal(HttpStatusCode.OK, withAlt.StatusCode);
    }

    private async Task<(HttpClient Client, string Token)> SignInAsync(string email)
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, email);
        await LoginWithTotpAsync(client, admin);
        var page = await client.GetAsync("/admin/logout");
        page.EnsureSuccessStatusCode();
        return (client, ExtractToken(await page.Content.ReadAsStringAsync()));
    }

    private static byte[] NextPhoto(int width, int height) =>
        MediaTestImages.Photo(width, height, seed: Interlocked.Increment(ref _seed));

    private static MultipartFormDataContent Multipart(string field, string fileName, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, field, fileName);
        return content;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, byte[] bytes, string fileName = "photo.jpg")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiPath) { Content = Multipart("file", fileName, bytes) };
        request.Headers.Add("RequestVerificationToken", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ContentPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("RequestVerificationToken", token);
        return client.SendAsync(request);
    }

    private static async Task<MediaErrorResponse> ReadErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<MediaErrorResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    private static async Task<string[]> ReadErrorsAsync(HttpResponseMessage response) =>
        [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray().Select(e => e.GetString()!)];

    private sealed class LyingLengthContent : HttpContent
    {
        private readonly long _length;

        public LyingLengthContent(long length)
        {
            _length = length;
            Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data") { Parameters = { new NameValueHeaderValue("boundary", "x") } };
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
