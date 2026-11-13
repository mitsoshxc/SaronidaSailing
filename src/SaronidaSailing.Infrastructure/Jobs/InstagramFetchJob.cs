using Hangfire;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Jobs;

public sealed class InstagramFetchJob(IInstagramFeedService feed)
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct) => await feed.FetchAsync(ct);
}
