namespace SaronidaSailing.Core.Interfaces;

public interface IPageCacheInvalidator
{
    Task EvictPagesAsync(IReadOnlyCollection<string> pageKeys, CancellationToken ct = default);
}
