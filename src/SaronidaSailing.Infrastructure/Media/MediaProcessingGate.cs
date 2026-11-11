namespace SaronidaSailing.Infrastructure.Media;

public sealed class MediaProcessingGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct = default) =>
        _semaphore.WaitAsync(timeout, ct);

    public void Release() => _semaphore.Release();
}
