using Microsoft.Extensions.Logging.Abstractions;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Caching;

namespace SaronidaSailing.Tests;

public sealed class OutputCachePageInvalidatorTests
{
    private readonly List<string> _events = [];

    private OutputCachePageInvalidator Create(ICacheEvictionBus? bus = null) =>
        new(new RecordingEvictor(_events), bus ?? new RecordingBus(_events), NullLogger<OutputCachePageInvalidator>.Instance);

    [Fact]
    public async Task EvictPages_EvictsEachPageTagLocally_AndPublishesTheSameTags()
    {
        await Create().EvictPagesAsync(["home", "boat"]);

        Assert.Equal(["evict:page:home,page:boat", "publish:page:home,page:boat"], _events);
    }

    [Fact]
    public async Task EvictPages_SharedEvictsOnlyAllPages_LocallyAndPublished()
    {
        await Create().EvictPagesAsync(["home", "shared"]);

        Assert.Equal(["evict:pages:all", "publish:pages:all"], _events);
    }

    [Fact]
    public async Task EvictPages_PublishFailure_IsSwallowed_AfterLocalEviction()
    {
        await Create(new ThrowingBus()).EvictPagesAsync(["home"]);

        Assert.Equal(["evict:page:home"], _events);
    }

    [Fact]
    public async Task EvictPages_PublishCancellation_Propagates()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Create(new ThrowingBus(new OperationCanceledException())).EvictPagesAsync(["home"]));
    }

    private sealed class RecordingEvictor(List<string> events) : ICacheTagEvictor
    {
        public Task EvictTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct = default)
        {
            events.Add("evict:" + string.Join(",", tags));
            return Task.CompletedTask;
        }

        public Task EvictAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingBus(List<string> events) : ICacheEvictionBus
    {
        public Task PublishAsync(IReadOnlyCollection<string> tags, CancellationToken ct = default)
        {
            events.Add("publish:" + string.Join(",", tags));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingBus(Exception? exception = null) : ICacheEvictionBus
    {
        public Task PublishAsync(IReadOnlyCollection<string> tags, CancellationToken ct = default) =>
            throw (exception ?? new InvalidOperationException("db down"));
    }
}
