using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class InstagramScheduleSync(
    IContentSectionRepository sections,
    IInstagramJobScheduler scheduler,
    ILogger<InstagramScheduleSync> logger) : IInstagramScheduleSync
{
    public async Task<bool> IsSectionActiveAsync(CancellationToken ct = default)
    {
        if (!ContentKeys.TryParseSection(InstagramSection.Key, out var page, out var section))
        {
            throw new InvalidOperationException($"Invalid section key '{InstagramSection.Key}'.");
        }

        var row = await sections.FindAsync(page, section, ct);
        return row?.IsActive ?? true;
    }

    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        var active = await IsSectionActiveAsync(ct);
        if (active)
        {
            scheduler.Register();
        }
        else
        {
            scheduler.Remove();
        }

        logger.LogInformation(
            "Instagram schedules synced at startup: {Action} (section {SectionKey} active={IsActive}).",
            active ? "registered" : "removed", InstagramSection.Key, active);
        return active;
    }
}
