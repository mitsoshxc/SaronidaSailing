using Microsoft.EntityFrameworkCore;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Repositories;

public sealed class IntegrationSettingRepository(AppDbContext db) : IIntegrationSettingRepository
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        (await db.Set<IntegrationSetting>().AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct))?.Value;

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var setting = await FindAsync(key, ct);
        if (setting is null)
        {
            db.Set<IntegrationSetting>().Add(IntegrationSetting.Create(key, value));
            return;
        }

        setting.SetValue(value);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var setting = await FindAsync(key, ct);
        if (setting is not null)
        {
            db.Set<IntegrationSetting>().Remove(setting);
        }
    }

    private async Task<IntegrationSetting?> FindAsync(string key, CancellationToken ct) =>
        db.Set<IntegrationSetting>().Local.FirstOrDefault(x => x.Key == key && db.Entry(x).State != EntityState.Deleted)
        ?? await db.Set<IntegrationSetting>().FirstOrDefaultAsync(x => x.Key == key, ct);
}
