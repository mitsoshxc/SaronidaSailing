using System.Globalization;

namespace SaronidaSailing.Core.Services;

internal static class InstagramTimestamps
{
    public static string Format(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTime? Parse(string? value) =>
        value is not null && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
