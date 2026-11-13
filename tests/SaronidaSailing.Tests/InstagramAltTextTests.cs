using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Tests;

public class InstagramAltTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n \r\n  ")]
    public void EmptyCaption_GivesNull(string? caption) => Assert.Null(InstagramAltText.FromCaption(caption));

    [Fact]
    public void ShortCaption_IsKept() => Assert.Equal("Sunset at sea", InstagramAltText.FromCaption("Sunset at sea"));

    [Fact]
    public void OnlyTheFirstLine_IsUsed() =>
        Assert.Equal("First line", InstagramAltText.FromCaption("First line\nsecond line\r\nthird #tag"));

    [Fact]
    public void LeadingBlankLines_AreSkipped() => Assert.Equal("Hello", InstagramAltText.FromCaption("\n \r\n  Hello  \nworld"));

    [Fact]
    public void Whitespace_IsCollapsed() => Assert.Equal("a b c d", InstagramAltText.FromCaption("a   b\t c   d"));

    [Fact]
    public void ExactlyMaxLength_IsNotCut()
    {
        var text = new string('a', InstagramAltText.MaxLength);

        Assert.Equal(text, InstagramAltText.FromCaption(text));
    }

    [Fact]
    public void LongCaption_IsCutAtAWordBoundary_WithEllipsis()
    {
        var caption = string.Join(' ', Enumerable.Repeat("sailing", 30));

        var alt = InstagramAltText.FromCaption(caption)!;

        Assert.True(alt.Length <= InstagramAltText.MaxLength);
        Assert.EndsWith("sailing…", alt);
        Assert.DoesNotContain(" …", alt);
        Assert.Equal(string.Join(' ', Enumerable.Repeat("sailing", alt.Count(c => c == ' ') + 1)) + "…", alt);
    }

    [Fact]
    public void LongCaption_CutExactlyAtASpace_KeepsWholeWords()
    {
        var caption = new string('a', InstagramAltText.MaxLength - 2) + " tail words";

        var alt = InstagramAltText.FromCaption(caption)!;

        Assert.Equal(new string('a', InstagramAltText.MaxLength - 2) + "…", alt);
    }

    [Fact]
    public void LongSingleWord_IsHardCut()
    {
        var alt = InstagramAltText.FromCaption(new string('b', 300))!;

        Assert.Equal(InstagramAltText.MaxLength, alt.Length);
        Assert.EndsWith("…", alt);
    }

    [Fact]
    public void HardCut_NeverSplitsASurrogatePair()
    {
        var alt = InstagramAltText.FromCaption("a" + string.Concat(Enumerable.Repeat("😀", 100)))!;

        Assert.True(alt.Length <= InstagramAltText.MaxLength);
        Assert.False(char.IsHighSurrogate(alt[^2]));
    }
}

public class InstagramPostCountTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("6", 6)]
    [InlineData("12", 12)]
    [InlineData(null, 6)]
    [InlineData("", 6)]
    [InlineData("0", 6)]
    [InlineData("13", 6)]
    [InlineData("abc", 6)]
    [InlineData(" 6", 6)]
    [InlineData("3 ", 6)]
    [InlineData("+3", 6)]
    [InlineData("-3", 6)]
    [InlineData("3.0", 6)]
    [InlineData("٣", 6)]
    public void Parse_AcceptsOnlyWholeNumbers1To12(string? value, int expected) =>
        Assert.Equal(expected, InstagramPostCount.Parse(value));

    [Theory]
    [InlineData("1", true)]
    [InlineData("12", true)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("13", false)]
    [InlineData("x", false)]
    [InlineData(" 6", false)]
    [InlineData("+6", false)]
    [InlineData("100", false)]
    public void IsValid_MatchesParse(string value, bool expected) => Assert.Equal(expected, InstagramPostCount.IsValid(value));
}
