using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Models;

public sealed record TripTab(string Id, string SectionKey, UiString Long, UiString Short);

public static class TripTabs
{
    public const string QueryKey = "trip";

    public static IReadOnlyList<TripTab> All { get; } =
    [
        new("day", "trips.day", UiText.TripDayLong, UiText.TripDayShort),
        new("islands", "trips.islands", UiText.TripIslandsLong, UiText.TripIslandsShort),
        new("week", "trips.week", UiText.TripWeekLong, UiText.TripWeekShort),
    ];

    public static string Normalize(string? requested) =>
        All.FirstOrDefault(t => string.Equals(t.Id, requested?.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
        ?? All[0].Id;

    public static string? Resolve(string? requested, Func<TripTab, bool> isActive)
    {
        var wanted = Normalize(requested);
        var chosen = All.First(t => t.Id == wanted);
        if (isActive(chosen))
        {
            return chosen.Id;
        }

        return All.FirstOrDefault(isActive)?.Id;
    }
}
