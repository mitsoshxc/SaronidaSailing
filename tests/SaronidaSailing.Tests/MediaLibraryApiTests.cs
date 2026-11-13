using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Endpoints;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public sealed class MediaLibraryApiTests : IAsyncLifetime
{
    private const string ApiPath = "/admin/api/media";

    private readonly TempDatabaseWebApplicationFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Library_ListsNewestFirst()
    {
        var oldest = await AddAssetAsync("oldest");
        var middle = await AddAssetAsync("middle");
        var newest = await AddAssetAsync("newest");
        var client = await SignInAsync("library-order@example.com");

        var response = await client.GetAsync(ApiPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([newest, middle, oldest], body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.False(body.GetProperty("hasMore").GetBoolean());
        var first = body.GetProperty("items")[0];
        Assert.False(first.TryGetProperty("alreadyExisted", out _));
        Assert.Equal(960, first.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("width").GetInt32()).ElementAt(1));
        Assert.EndsWith("-960.webp", first.GetProperty("previewUrl").GetString());
        Assert.EndsWith("-480.webp", first.GetProperty("thumbUrl").GetString());
    }

    [Fact]
    public async Task Library_PagesBy48()
    {
        for (var i = 0; i < 49; i++)
        {
            await AddAssetAsync($"asset-{i}");
        }

        var client = await SignInAsync("library-paging@example.com");

        var first = await (await client.GetAsync($"{ApiPath}?page=1")).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.GetAsync($"{ApiPath}?page=2")).Content.ReadFromJsonAsync<JsonElement>();
        var third = await (await client.GetAsync($"{ApiPath}?page=3")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(48, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Equal("asset-0.jpg", second.GetProperty("items")[0].GetProperty("originalFileName").GetString());
        Assert.Equal(0, third.GetProperty("items").GetArrayLength());
        Assert.False(third.GetProperty("hasMore").GetBoolean());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("99999999999")]
    [InlineData("2147483647")]
    public async Task Library_InvalidPage_Is400(string page)
    {
        var client = await SignInAsync("library-invalid@example.com");

        var response = await client.GetAsync($"{ApiPath}?page={page}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<MediaErrorResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("invalidPage", error!.Code);
    }

    [Fact]
    public async Task GetById_WithoutA960File_UsesTheLargestForPreview()
    {
        var id = await AddAssetAsync("small", [480, 700]);
        var client = await SignInAsync("library-get@example.com");

        var body = await (await client.GetAsync($"{ApiPath}/{id}")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.EndsWith("-700.webp", body.GetProperty("previewUrl").GetString());
        Assert.EndsWith("-480.webp", body.GetProperty("thumbUrl").GetString());
        Assert.Equal(2, body.GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public async Task InstagramAssets_AreNotInTheLibrary_AndGetReturnsNotFound()
    {
        var upload = await AddAssetAsync("mine");
        var instagram = await AddAssetAsync("from-instagram", source: MediaSource.Instagram);
        var client = await SignInAsync("library-instagram@example.com");

        var list = await (await client.GetAsync(ApiPath)).Content.ReadFromJsonAsync<JsonElement>();
        var single = await client.GetAsync($"{ApiPath}/{instagram}");

        Assert.Equal([upload], list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, single.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{ApiPath}/{upload}")).StatusCode);
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = NewClient(_factory);
        await LoginWithTotpAsync(client, await AddTwoFactorAdminAsync(_factory, email));
        return client;
    }

    private async Task<Guid> AddAssetAsync(string name, int[]? widths = null, MediaSource source = MediaSource.Upload)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
        var asset = MediaAsset.Create($"{name}.jpg", hash, name, widths ?? [480, 960, 1600], 3000, 2000, ".jpg", source: source);
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>().Add(asset);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await Task.Delay(5);
        return asset.Id;
    }
}
