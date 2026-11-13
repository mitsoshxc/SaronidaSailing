namespace SaronidaSailing.Core.Interfaces;

public interface IDistributedLock
{
    Task<IAsyncDisposable> AcquireAsync(string resource, TimeSpan timeout, CancellationToken ct = default);
}
