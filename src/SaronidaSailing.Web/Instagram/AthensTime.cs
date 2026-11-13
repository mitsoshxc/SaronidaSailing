using System.Globalization;

namespace SaronidaSailing.Web.Instagram;

public static class AthensTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Athens");

    public static DateTime ToAthens(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static string Format(DateTime utc) =>
        ToAthens(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
