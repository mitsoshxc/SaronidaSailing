using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Endpoints;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class ContentApiTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private const string ApiPath = "/admin/api/content";

    [Fact]
    public async Task Anonymous_GetsUnauthorizedNotARedirect()
    {
        var client = NewClient(factory);

        var post = await client.PostAsync(ApiPath, Json("""{"changes":[]}"""));
        var history = await client.GetAsync($"{ApiPath}/history?key=home.hero.title&locale=el");
        var save = await client.GetAsync($"{ApiPath}/saves/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, history.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, save.StatusCode);
        Assert.Null(post.Headers.Location);
    }

    [Fact]
    public async Task SignedInWithoutTwoFactor_GetsForbiddenNotARedirect()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "api-no2fa@example.com");
        var login = await LoginAsync(client, "api-no2fa@example.com");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var post = await client.PostAsync(ApiPath, Json("""{"changes":[]}"""));
        var history = await client.GetAsync($"{ApiPath}/history?key=home.hero.title&locale=el");

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, history.StatusCode);
        Assert.Null(post.Headers.Location);
    }

    [Fact]
    public async Task NonApiPaths_StillRedirectToLogin()
    {
        var client = NewClient(factory);

        var response = await client.GetAsync("/admin/jobs");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/login", Location(response));
    }

    [Fact]
    public async Task MissingOrWrongAntiforgeryToken_IsRejected()
    {
        var (client, _) = await SignInAsync("api-csrf@example.com");

        var missing = await client.PostAsync(ApiPath, Json("""{"changes":[]}"""));
        var request = new HttpRequestMessage(HttpMethod.Post, ApiPath) { Content = Json("""{"changes":[]}""") };
        request.Headers.Add("RequestVerificationToken", "not-a-token");
        var wrong = await client.SendAsync(request);

        foreach (var response in new[] { missing, wrong })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(["Invalid antiforgery token."], await ReadErrorsAsync(response));
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task ValidSave_Returns200WithSaveIdAndContentIsReadable()
    {
        var (client, token) = await SignInAsync("api-save@example.com");

        var response = await PostAsync(
            client,
            token,
            """
            {"changes":[
              {"op":"setField","key":"apitest.hero.title","locale":"el","type":"text","value":"Γεια σου"},
              {"op":"setField","key":"apitest.hero.body","locale":"el","type":"richText","value":"<p>Hi <b>there</b></p><script>x()</script>"}
            ]}
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        var saveId = saved.GetProperty("saveId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IContentService>().GetPageContentAsync("apitest", "el");
        Assert.Equal("Γεια σου", page.GetField("apitest.hero.title")?.Value);
        Assert.Equal("<p>Hi there</p>", page.GetField("apitest.hero.body")?.Value);
        Assert.Equal(saveId, (await scope.ServiceProvider.GetRequiredService<IContentService>().GetSaveAsync(saveId))?.SaveId);
    }

    [Fact]
    public async Task AllChangeKinds_AreAccepted()
    {
        var (client, token) = await SignInAsync("api-ops@example.com");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var response = await PostAsync(
            client,
            token,
            $$"""
            {"changes":[
              {"op":"addListItem","listKey":"apiops.items","id":"{{first}}"},
              {"op":"addListItem","listKey":"apiops.items","id":"{{second}}"},
              {"op":"setField","key":"apiops.items.title","locale":"el","type":"text","value":"x","listItemId":"{{first}}"},
              {"op":"reorderList","listKey":"apiops.items","orderedIds":["{{second}}","{{first}}"]},
              {"op":"setListItemActive","listKey":"apiops.items","id":"{{first}}","isActive":false},
              {"op":"setSectionActive","sectionKey":"apiops.items","isActive":false},
              {"op":"deleteListItem","listKey":"apiops.items","id":"{{second}}"}
            ]}
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IContentService>().GetPageContentAsync("apiops", "el");
        var entry = Assert.Single(page.GetList("apiops.items"));
        Assert.Equal((first, false), (entry.Id, entry.IsActive));
        Assert.False(page.IsSectionActive("apiops.items"));
    }

    [Fact]
    public async Task EmptyChangeList_Returns200WithNullSaveId()
    {
        var (client, token) = await SignInAsync("api-empty@example.com");

        var response = await PostAsync(client, token, """{"changes":[]}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("saveId").ValueKind);
    }

    [Fact]
    public async Task RejectedBatch_Returns400WithErrorsAndWritesNothing()
    {
        var (client, token) = await SignInAsync("api-reject@example.com");

        var response = await PostAsync(
            client,
            token,
            """
            {"changes":[
              {"op":"setField","key":"apireject.hero.title","locale":"el","type":"text","value":"a"},
              {"op":"setField","key":"apireject.hero.cta","locale":"el","type":"link","value":"{\"url\":\"http://x.com\",\"label\":\"Go\"}"},
              {"op":"setField","key":"bad","locale":"el","type":"text","value":"a"}
            ]}
            """);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.Equal(2, errors.Length);
        Assert.Contains(errors, e => e.StartsWith("Invalid link for 'apireject.hero.cta' (el)"));
        using var scope = factory.Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IContentService>().GetPageContentAsync("apireject", "el");
        Assert.Null(page.GetField("apireject.hero.title"));
    }

    [Theory]
    [InlineData("""{"changes":[{"op":"explode","key":"a.b.c"}]}""")]
    [InlineData("""{"changes":[{"key":"a.b.c","locale":"el","type":"text","value":"x"}]}""")]
    [InlineData("""{"changes":[{"op":"setField","key":"a.b.c","locale":"el","type":"text"}]}""")]
    [InlineData("""{"changes":[{"op":"setField","key":"a.b.c","locale":"el","type":"video","value":"x"}]}""")]
    [InlineData("""{"changes":[{"op":"setField","key":"a.b.c","locale":"el","type":0,"value":"x"}]}""")]
    [InlineData("""{"changes":[{"op":"addListItem","listKey":"a.b"}]}""")]
    [InlineData("""{"changes":[{"op":"addListItem","listKey":"a.b","id":"nope"}]}""")]
    [InlineData("""{"changes":[null]}""")]
    [InlineData("""{"changes":null}""")]
    [InlineData("""{"changes":[{"op":"setField","key":null,"locale":"el","type":"text","value":"x"}]}""")]
    [InlineData("""{"changes":[{"op":"setField","key":"home.hero.title","locale":null,"type":"text","value":"x"}]}""")]
    [InlineData("""{"changes":[{"op":"setField","key":"home.hero.title","locale":"el","type":"text","value":null}]}""")]
    [InlineData("""{"changes":[{"op":"addListItem","listKey":null,"id":"6f1c5a0e-2b7d-4c55-9a1e-3d2f8b7c9e10"}]}""")]
    [InlineData("""{"changes":[{"op":"reorderList","listKey":"home.items","orderedIds":null}]}""")]
    [InlineData("""{"changes":[{"op":"setSectionActive","sectionKey":null,"isActive":true}]}""")]
    [InlineData("""{}""")]
    [InlineData("""null""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public async Task MalformedBody_Returns400WithErrors(string body)
    {
        var (client, token) = await SignInAsync($"api-bad-{Guid.NewGuid():N}@example.com");

        var response = await PostAsync(client, token, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await ReadErrorsAsync(response));
    }

    [Fact]
    public async Task BodyOverLimit_Returns413()
    {
        var (client, token) = await SignInAsync("api-big@example.com");
        var value = new string('a', ContentEndpoints.MaxBodyBytes);
        var body = $$"""{"changes":[{"op":"setField","key":"apibig.hero.title","locale":"el","type":"text","value":"{{value}}"}]}""";

        var response = await PostAsync(client, token, body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.NotEmpty(await ReadErrorsAsync(response));
        using var scope = factory.Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IContentService>().GetPageContentAsync("apibig", "el");
        Assert.Null(page.GetField("apibig.hero.title"));
    }

    [Fact]
    public async Task BodyOverLimitWithoutContentLength_Returns413()
    {
        var (client, token) = await SignInAsync("api-chunked@example.com");
        var value = new string('a', ContentEndpoints.MaxBodyBytes);
        var body = $$"""{"changes":[{"op":"setField","key":"apichunked.hero.title","locale":"el","type":"text","value":"{{value}}"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, ApiPath) { Content = new UnknownLengthContent(body) };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task BodyJustUnderLimit_IsAccepted()
    {
        var (client, token) = await SignInAsync("api-under@example.com");
        var value = new string('a', ContentEndpoints.MaxBodyBytes - 200);
        var body = $$"""{"changes":[{"op":"setField","key":"apiunder.hero.title","locale":"el","type":"text","value":"{{value}}"}]}""";

        var response = await PostAsync(client, token, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task History_ReturnsVersionsNewestFirst_AndRejectsBadInput()
    {
        var (client, token) = await SignInAsync("api-history@example.com");
        var first = await SaveFieldAsync(client, token, "apihist.hero.title", "v1");
        await Task.Delay(20);
        var second = await SaveFieldAsync(client, token, "apihist.hero.title", "v2");

        var response = await client.GetAsync($"{ApiPath}/history?key=apihist.hero.title&locale=el");
        var unknown = await client.GetAsync($"{ApiPath}/history?key=apihist.hero.other&locale=el");
        var badKey = await client.GetAsync($"{ApiPath}/history?key=bad&locale=el");
        var badLocale = await client.GetAsync($"{ApiPath}/history?key=apihist.hero.title&locale=fr");
        var badItem = await client.GetAsync($"{ApiPath}/history?key=apihist.hero.title&locale=el&listItemId=nope");
        var noKey = await client.GetAsync($"{ApiPath}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var versions = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["v2", "v1"], versions.EnumerateArray().Select(v => v.GetProperty("value").GetString()));
        var newest = versions[0];
        Assert.Equal(second, newest.GetProperty("saveId").GetGuid());
        Assert.Equal(first, versions[1].GetProperty("saveId").GetGuid());
        Assert.Equal("api-history@example.com", newest.GetProperty("changedBy").GetString());
        Assert.True(newest.GetProperty("createdAt").GetDateTime() > DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(0, (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        foreach (var bad in new[] { badKey, badLocale, badItem, noKey })
        {
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.NotEmpty(await ReadErrorsAsync(bad));
            Assert.Equal("no-store", bad.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task Save_ReturnsFieldsWithPreviousValues_AndUnknownIdIs404()
    {
        var (client, token) = await SignInAsync("api-saves@example.com");
        await SaveFieldAsync(client, token, "apisave.hero.title", "old");
        var saveId = await SaveFieldAsync(client, token, "apisave.hero.title", "new");

        var response = await client.GetAsync($"{ApiPath}/saves/{saveId}");
        var unknown = await client.GetAsync($"{ApiPath}/saves/{Guid.NewGuid()}");
        var malformed = await client.GetAsync($"{ApiPath}/saves/not-a-guid");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var details = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saveId, details.GetProperty("saveId").GetGuid());
        Assert.Equal("api-saves@example.com", details.GetProperty("changedBy").GetString());
        var field = Assert.Single(details.GetProperty("fields").EnumerateArray());
        Assert.Equal("apisave.hero.title", field.GetProperty("key").GetString());
        Assert.Equal("el", field.GetProperty("locale").GetString());
        Assert.Equal("text", field.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, field.GetProperty("listItemId").ValueKind);
        Assert.Equal("new", field.GetProperty("value").GetString());
        Assert.Equal("old", field.GetProperty("previousValue").GetString());
        Assert.Equal("new", field.GetProperty("currentValue").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("no-store", unknown.Headers.CacheControl?.ToString());
        Assert.NotEmpty(await ReadErrorsAsync(unknown));
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
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

    private static async Task<Guid> SaveFieldAsync(HttpClient client, string token, string key, string value)
    {
        var response = await PostAsync(
            client,
            token,
            $$"""{"changes":[{"op":"setField","key":"{{key}}","locale":"el","type":"text","value":"{{value}}"}]}""");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saveId").GetGuid();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiPath) { Content = Json(body) };
        request.Headers.Add("RequestVerificationToken", token);
        return client.SendAsync(request);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<string[]> ReadErrorsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!)];
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnknownLengthContent(string body)
        {
            _bytes = Encoding.UTF8.GetBytes(body);
            Headers.ContentType = new("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes, 0, _bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
