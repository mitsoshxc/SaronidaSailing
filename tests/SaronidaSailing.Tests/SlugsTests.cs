using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Tests;

public sealed class SlugsTests
{
    [Theory]
    [InlineData("Ιστιοπλοΐα Σαρωνίδα.JPG", "istioploia-saronida")]
    [InlineData("Ουρανός.png", "ouranos")]
    [InlineData("ΟΥΡΑΝΟΣ.png", "ouranos")]
    [InlineData("Θάλασσα ψάρι χαρά.webp", "thalassa-psari-chara")]
    [InlineData("Ξενοδοχείο Ζωή.jpg", "xenodocheio-zoi")]
    [InlineData("φωτογραφία ΰδωρ ϊδέα.jpg", "fotografia-ydor-idea")]
    [InlineData("Σαρωνίδα ς.jpg", "saronida-s")]
    [InlineData("Café déjà vu.jpeg", "cafe-deja-vu")]
    [InlineData("Ångström_Ünïcode.jpg", "angstrom-unicode")]
    [InlineData("IMG_1234 (copy).JPG", "img-1234-copy")]
    [InlineData("--a!!!b___c..d--.png", "a-b-c-d")]
    [InlineData("noextension", "noextension")]
    public void FromFileName_TransliteratesAndNormalizes(string fileName, string expected) =>
        Assert.Equal(expected, Slugs.FromFileName(fileName));

    [Theory]
    [InlineData("")]
    [InlineData(".jpg")]
    [InlineData("!!!.png")]
    [InlineData("---")]
    [InlineData("日本語.jpg")]
    public void FromFileName_NothingLeft_IsImage(string fileName) =>
        Assert.Equal("image", Slugs.FromFileName(fileName));

    [Fact]
    public void FromFileName_LongName_IsCutTo50Characters()
    {
        var slug = Slugs.FromFileName(new string('a', 80) + ".jpg");

        Assert.Equal(new string('a', 50), slug);
    }

    [Fact]
    public void FromFileName_CutInsideADash_DoesNotEndWithDash()
    {
        var slug = Slugs.FromFileName(new string('a', 49) + "-bbbb.jpg");

        Assert.Equal(new string('a', 49), slug);
    }

    [Fact]
    public void FromFileName_OnlyKeepsLowercaseAsciiLettersDigitsAndDashes()
    {
        var slug = Slugs.FromFileName("Ταξίδι στη ΣΑΡΩΝΙΔΑ 2026 – Photo #7 ✓.JPEG");

        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", slug);
        Assert.Equal("taxidi-sti-saronida-2026-photo-7", slug);
    }
}
