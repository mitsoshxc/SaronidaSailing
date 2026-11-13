using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class InstagramWarningService(
    IInstagramScheduleSync schedule,
    IInstagramConnectionService connection) : IInstagramWarningService
{
    public async Task<InstagramWarning> GetToolbarWarningAsync(CancellationToken ct = default)
    {
        if (!await schedule.IsSectionActiveAsync(ct))
        {
            return InstagramWarning.None;
        }

        return (await connection.GetStatusAsync(ct)).State switch
        {
            InstagramConnectionState.NotConnected => InstagramWarning.NotConnected,
            InstagramConnectionState.Disconnected => InstagramWarning.Disconnected,
            _ => InstagramWarning.None,
        };
    }
}
