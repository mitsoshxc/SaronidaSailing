namespace SaronidaSailing.Core.Interfaces;

public enum InstagramWarning
{
    None,
    NotConnected,
    Disconnected
}

public interface IInstagramWarningService
{
    Task<InstagramWarning> GetToolbarWarningAsync(CancellationToken ct = default);
}
