using System.Reflection;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Jobs;

namespace SaronidaSailing.Tests;

public class InstagramJobTests
{
    [Theory]
    [InlineData(typeof(InstagramFetchJob))]
    [InlineData(typeof(InstagramTokenRefreshJob))]
    public void Jobs_NeverRetry_AndNeverOverlap(Type jobType)
    {
        var method = jobType.GetMethod("RunAsync")!;

        var retry = method.GetCustomAttribute<AutomaticRetryAttribute>();
        var concurrency = method.GetCustomAttribute<DisableConcurrentExecutionAttribute>();

        Assert.NotNull(retry);
        Assert.Equal(0, retry.Attempts);
        Assert.NotNull(concurrency);
        Assert.Equal(600, concurrency.TimeoutSec);
    }

    [Fact]
    public void RecurringJobIds_AreStable()
    {
        Assert.Equal("instagram-fetch", InstagramJobIds.Fetch);
        Assert.Equal("instagram-token-refresh", InstagramJobIds.TokenRefresh);
    }

    [Fact]
    public void EnqueueFetch_EnqueuesTheFetchJobOnce()
    {
        var client = new RecordingJobClient();

        new InstagramJobQueue(client).EnqueueFetch();

        var created = Assert.Single(client.Created);
        Assert.Equal(typeof(InstagramFetchJob), created.Job.Type);
        Assert.Equal("RunAsync", created.Job.Method.Name);
        Assert.IsType<EnqueuedState>(created.State);
    }

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<(Job Job, IState State)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add((job, state));
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => throw new NotSupportedException();
    }
}
