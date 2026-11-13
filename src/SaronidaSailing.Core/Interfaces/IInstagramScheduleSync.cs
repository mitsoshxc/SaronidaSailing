namespace SaronidaSailing.Core.Interfaces;

public interface IInstagramScheduleSync
{
    Task<bool> IsSectionActiveAsync(CancellationToken ct = default);

    Task<bool> SyncAsync(CancellationToken ct = default);
}
