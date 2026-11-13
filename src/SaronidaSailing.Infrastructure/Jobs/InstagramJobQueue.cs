using Hangfire;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Jobs;

public sealed class InstagramJobQueue(IBackgroundJobClient client) : IInstagramJobQueue
{
    public void EnqueueFetch() => client.Enqueue<InstagramFetchJob>(job => job.RunAsync(CancellationToken.None));
}
