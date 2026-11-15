namespace SaronidaSailing.Core.Interfaces;

public interface ISiteSettingsService
{
    Task<string?> GetRobotsTxtAsync(CancellationToken ct = default);

    Task<SiteSettingsSaveResult> SaveRobotsTxtAsync(string? text, string? adminEmail, string? ipAddress, CancellationToken ct = default);
}

public sealed record SiteSettingsSaveResult(bool Succeeded, string? Error);
