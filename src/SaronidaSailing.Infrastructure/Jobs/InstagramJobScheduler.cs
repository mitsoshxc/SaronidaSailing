using Hangfire;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Infrastructure.Jobs;

public sealed class InstagramJobScheduler(IRecurringJobManager manager) : IInstagramJobScheduler
{
    public const string TimeZoneId = "Europe/Athens";

    public void Register()
    {
        var options = new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId) };
        manager.AddOrUpdate<InstagramFetchJob>(
            InstagramJobIds.Fetch, job => job.RunAsync(CancellationToken.None), InstagramJobIds.FetchCron, options);
        manager.AddOrUpdate<InstagramTokenRefreshJob>(
            InstagramJobIds.TokenRefresh, job => job.RunAsync(CancellationToken.None), InstagramJobIds.TokenRefreshCron, options);
    }

    public void Remove()
    {
        manager.RemoveIfExists(InstagramJobIds.Fetch);
        manager.RemoveIfExists(InstagramJobIds.TokenRefresh);
    }
}
