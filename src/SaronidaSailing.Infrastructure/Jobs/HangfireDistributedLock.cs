using Hangfire;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Jobs;

public sealed class HangfireDistributedLock(JobStorage storage) : IDistributedLock
{
    public Task<IAsyncDisposable> AcquireAsync(string resource, TimeSpan timeout, CancellationToken ct = default) =>
        Task.Run<IAsyncDisposable>(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                var connection = storage.GetConnection();
                try
                {
                    return new Lease(connection, connection.AcquireDistributedLock(resource, timeout));
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            },
            ct);

    private sealed class Lease(IDisposable connection, IDisposable held) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            held.Dispose();
            connection.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
