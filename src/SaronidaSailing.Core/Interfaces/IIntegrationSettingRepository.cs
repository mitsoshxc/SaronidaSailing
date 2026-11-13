namespace SaronidaSailing.Core.Interfaces;

public interface IIntegrationSettingRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string value, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}
