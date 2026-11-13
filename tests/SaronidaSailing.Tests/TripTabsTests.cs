using SaronidaSailing.Web.Models;

namespace SaronidaSailing.Tests;

public class TripTabsTests
{
    private static readonly Func<TripTab, bool> AllActive = _ => true;

    [Theory]
    [InlineData(null, "day")]
    [InlineData("", "day")]
    [InlineData("day", "day")]
    [InlineData("islands", "islands")]
    [InlineData("week", "week")]
    [InlineData("ISLANDS", "islands")]
    [InlineData("Week", "week")]
    [InlineData(" week ", "week")]
    [InlineData("bogus", "day")]
    [InlineData("day,week", "day")]
    public void Normalize_FallsBackToDay(string? requested, string expected) =>
        Assert.Equal(expected, TripTabs.Normalize(requested));

    [Theory]
    [InlineData(null, "day")]
    [InlineData("bogus", "day")]
    [InlineData("islands", "islands")]
    [InlineData("WEEK", "week")]
    public void Resolve_ReturnsTheRequestedTrip_WhenActive(string? requested, string expected) =>
        Assert.Equal(expected, TripTabs.Resolve(requested, AllActive));

    [Fact]
    public void Resolve_InactiveRequestedTrip_FallsBackToTheFirstActiveOne()
    {
        Assert.Equal("day", TripTabs.Resolve("islands", t => t.Id != "islands"));
        Assert.Equal("islands", TripTabs.Resolve("day", t => t.Id != "day"));
        Assert.Equal("week", TripTabs.Resolve("day", t => t.Id == "week"));
        Assert.Equal("week", TripTabs.Resolve("bogus", t => t.Id == "week"));
    }

    [Fact]
    public void Resolve_AllInactive_ReturnsNull() => Assert.Null(TripTabs.Resolve("day", _ => false));

    [Theory]
    [InlineData("el")]
    [InlineData("en")]
    public void ShortLabels_AreContainedInTheLongNames(string locale)
    {
        foreach (var tab in TripTabs.All)
        {
            Assert.Contains(tab.Short.For(locale), tab.Long.For(locale), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Tabs_AreFixedInCodeInTheAgreedOrder()
    {
        Assert.Equal(["day", "islands", "week"], TripTabs.All.Select(t => t.Id));
        Assert.Equal(["trips.day", "trips.islands", "trips.week"], TripTabs.All.Select(t => t.SectionKey));
    }
}
