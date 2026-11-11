using System.Text.Json;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Localization;
using static SaronidaSailing.Tests.TagHelperTestSupport;

namespace SaronidaSailing.Tests;

public sealed class CmsEditorConfigTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly SitePage Home = new("home", "/Index", "", "");

    private static CmsSeoConfig Seo(
        string locale = "el",
        Dictionary<string, ContentValue>? fields = null,
        PageImage? photo = null)
    {
        var content = Content(
            locale,
            fields,
            images: photo is null ? [] : new Dictionary<Guid, PageImage> { [photo.Id] = photo });
        return CmsEditorConfig.Create(content, "token", Home).Seo!;
    }

    [Fact]
    public void NonSitePage_HasNoSeo() =>
        Assert.Null(CmsEditorConfig.Create(Content(), "token", null).Seo);

    [Fact]
    public void NoSeoImage_IsEmpty()
    {
        var seo = Seo();

        Assert.Equal(new CmsSeoImage("", ""), seo.Image);
        Assert.Equal(new CmsSeoAlt("", ""), seo.ImageAlt);
    }

    [Fact]
    public void SeoImage_UsesThe960Source()
    {
        var seo = Seo(fields: new() { ["home.seo.image"] = Image(Id) }, photo: Photo(Id));

        Assert.Equal(new CmsSeoImage(Id.ToString(), "/media/photo-960.webp"), seo.Image);
    }

    [Fact]
    public void SeoImage_Without960_UsesTheLargestSource()
    {
        var seo = Seo(fields: new() { ["home.seo.image"] = Image(Id) }, photo: Photo(Id, 3000, 2000, 320, 700));

        Assert.Equal("/media/photo-700.webp", seo.Image.PreviewUrl);
    }

    [Fact]
    public void SeoImage_ForEnglish_IsTheGreekImage()
    {
        var seo = Seo("en", new() { ["home.seo.image"] = Image(Id, fallback: true) }, Photo(Id));

        Assert.Equal(new CmsSeoImage(Id.ToString(), "/media/photo-960.webp"), seo.Image);
    }

    [Fact]
    public void SeoImage_UnknownAsset_KeepsTheValueWithoutPreview()
    {
        var seo = Seo(fields: new() { ["home.seo.image"] = Image(Id) });

        Assert.Equal(new CmsSeoImage(Id.ToString(), ""), seo.Image);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void SeoImage_BlankValue_IsEmpty(string value)
    {
        var seo = Seo(fields: new() { ["home.seo.image"] = new ContentValue(value, ContentType.Image, false) });

        Assert.Equal(new CmsSeoImage("", ""), seo.Image);
    }

    [Fact]
    public void SeoImage_WrongType_IsEmpty()
    {
        var seo = Seo(fields: new() { ["home.seo.image"] = Text(Id.ToString()) });

        Assert.Equal(new CmsSeoImage("", ""), seo.Image);
    }

    [Fact]
    public void SeoAlt_Greek_IsBothValues()
    {
        var seo = Seo(fields: new() { ["home.seo.image-alt"] = Text("Το σκάφος") });

        Assert.Equal(new CmsSeoAlt("Το σκάφος", "Το σκάφος"), seo.ImageAlt);
    }

    [Fact]
    public void SeoAlt_English_FallbackShowsOnlyTheGreekValue()
    {
        var seo = Seo("en", new()
        {
            ["home.seo.image-alt"] = new ContentValue("Το σκάφος", ContentType.Text, true) { DefaultLocaleValue = "Το σκάφος" },
        });

        Assert.Equal(new CmsSeoAlt("", "Το σκάφος"), seo.ImageAlt);
    }

    [Fact]
    public void SeoAlt_English_OwnValueCarriesTheGreekOne()
    {
        var seo = Seo("en", new()
        {
            ["home.seo.image-alt"] = new ContentValue("The boat", ContentType.Text, false) { DefaultLocaleValue = "Το σκάφος" },
        });

        Assert.Equal(new CmsSeoAlt("The boat", "Το σκάφος"), seo.ImageAlt);
    }

    [Fact]
    public void SeoAlt_English_OwnValueWithoutGreek_HasEmptyGreek()
    {
        var seo = Seo("en", new() { ["home.seo.image-alt"] = Text("The boat") });

        Assert.Equal(new CmsSeoAlt("The boat", ""), seo.ImageAlt);
    }

    [Fact]
    public void Json_HasTheImageAndAltObjects()
    {
        var content = Content(
            "en",
            new()
            {
                ["home.seo.image"] = Image(Id, fallback: true),
                ["home.seo.image-alt"] = new ContentValue("The boat", ContentType.Text, false) { DefaultLocaleValue = "Το σκάφος" },
            },
            images: new Dictionary<Guid, PageImage> { [Id] = Photo(Id) });

        using var json = JsonDocument.Parse(CmsEditorConfig.Create(content, "token", Home).ToJson());
        var seo = json.RootElement.GetProperty("seo");

        Assert.Equal(Id.ToString(), seo.GetProperty("image").GetProperty("value").GetString());
        Assert.Equal("/media/photo-960.webp", seo.GetProperty("image").GetProperty("previewUrl").GetString());
        Assert.Equal("The boat", seo.GetProperty("imageAlt").GetProperty("value").GetString());
        Assert.Equal("Το σκάφος", seo.GetProperty("imageAlt").GetProperty("greek").GetString());
    }
}
