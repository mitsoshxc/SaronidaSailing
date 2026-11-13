namespace SaronidaSailing.Core.Interfaces;

public interface ISectionActivationHandler
{
    string SectionKey { get; }

    Task OnActiveChangedAsync(bool isActive, CancellationToken ct);
}
