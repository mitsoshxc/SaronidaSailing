using System.Globalization;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class SiteSettingsService(
    IIntegrationSettingRepository settings,
    IUnitOfWork unitOfWork,
    IAuditLog audit) : ISiteSettingsService
{
    public const string RobotsTxtKey = "site.robots-txt";

    public const int RobotsTxtMaxLength = 10_000;

    public async Task<string?> GetRobotsTxtAsync(CancellationToken ct = default)
    {
        var stored = await settings.GetAsync(RobotsTxtKey, ct);
        return string.IsNullOrWhiteSpace(stored) ? null : stored;
    }

    public async Task<SiteSettingsSaveResult> SaveRobotsTxtAsync(
        string? text, string? adminEmail, string? ipAddress, CancellationToken ct = default)
    {
        var normalized = Normalize(text);
        if (normalized.Length > RobotsTxtMaxLength)
        {
            return new SiteSettingsSaveResult(false, $"robots.txt can have at most {RobotsTxtMaxLength.ToString("N0", CultureInfo.InvariantCulture)} characters.");
        }

        if (normalized.Length == 0)
        {
            await settings.RemoveAsync(RobotsTxtKey, ct);
        }
        else
        {
            await settings.SetAsync(RobotsTxtKey, normalized, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        audit.Record("robots-txt-saved", adminEmail, ipAddress, normalized.Length == 0 ? "default" : $"characters={normalized.Length}");
        return new SiteSettingsSaveResult(true, null);
    }

    private static string Normalize(string? text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd());
        var joined = string.Join('\n', lines).TrimEnd();
        return joined.Length == 0 ? "" : joined + "\n";
    }
}
