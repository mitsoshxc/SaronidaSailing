namespace SaronidaSailing.Core.Interfaces;

public interface ICacheTagEvictor
{
    Task EvictTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct = default);

    Task EvictAllAsync(CancellationToken ct = default);
}
