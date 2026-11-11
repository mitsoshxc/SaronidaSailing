namespace SaronidaSailing.Core.Interfaces;

public interface ICacheEvictionBus
{
    Task PublishAsync(IReadOnlyCollection<string> tags, CancellationToken ct = default);
}
