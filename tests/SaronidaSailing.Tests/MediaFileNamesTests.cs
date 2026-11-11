using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Tests;

public class MediaFileNamesTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void PublicFileName_UsesSlugFirst16HashCharsAndWidth()
    {
        Assert.Equal("hero-0123456789abcdef-960.webp", MediaFileNames.PublicFileName("hero", Hash, 960));
    }

    [Fact]
    public void PublicUrl_IsUnderMedia()
    {
        Assert.Equal("/media/hero-0123456789abcdef-480.webp", MediaFileNames.PublicUrl("hero", Hash, 480));
    }

    [Fact]
    public void OriginalFileName_IsHashPlusExtension()
    {
        Assert.Equal(Hash + ".jpg", MediaFileNames.OriginalFileName(Hash, ".jpg"));
    }

    [Fact]
    public void AssetOverloads_UseTheAssetFields()
    {
        var asset = MediaAsset.Create("  Boat.JPG ", Hash, "boat", [1600, 480, 960], 3000, 2000, ".jpg");

        Assert.Equal("Boat.JPG", asset.OriginalFileName);
        Assert.Equal("480,960,1600", asset.Widths);
        Assert.Equal([480, 960, 1600], asset.GetWidths());
        Assert.Equal("boat-0123456789abcdef-960.webp", MediaFileNames.PublicFileName(asset, 960));
        Assert.Equal("/media/boat-0123456789abcdef-1600.webp", MediaFileNames.PublicUrl(asset, 1600));
        Assert.Equal(Hash + ".jpg", MediaFileNames.OriginalFileName(asset));
    }
}
