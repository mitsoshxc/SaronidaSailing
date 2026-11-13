using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class InstagramSectionHandler(
    IInstagramJobScheduler scheduler,
    ILogger<InstagramSectionHandler> logger) : ISectionActivationHandler
{
    public string SectionKey => InstagramSection.Key;

    public Task OnActiveChangedAsync(bool isActive, CancellationToken ct)
    {
        if (isActive)
        {
            scheduler.Register();
        }
        else
        {
            scheduler.Remove();
        }

        logger.LogInformation(
            "Instagram schedules {Action} (section {SectionKey} active={IsActive}).",
            isActive ? "registered" : "removed", InstagramSection.Key, isActive);
        return Task.CompletedTask;
    }
}
