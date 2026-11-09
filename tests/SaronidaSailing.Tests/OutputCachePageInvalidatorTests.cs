using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Web.Caching;

namespace SaronidaSailing.Tests;

public sealed class OutputCachePageInvalidatorTests
{
    [Fact]
    public async Task EvictPages_EvictsEachPageTag()
    {
        var store = new RecordingStore();

        await new OutputCachePageInvalidator(store).EvictPagesAsync(["home", "boat"]);

        Assert.Equal(["page:home", "page:boat"], store.Tags);
    }

    [Fact]
    public async Task EvictPages_SharedEvictsOnlyAllPages()
    {
        var store = new RecordingStore();

        await new OutputCachePageInvalidator(store).EvictPagesAsync(["home", "shared"]);

        Assert.Equal(["pages:all"], store.Tags);
    }

    private sealed class RecordingStore : IOutputCacheStore
    {
        public List<string> Tags { get; } = [];

        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
        {
            Tags.Add(tag);
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
