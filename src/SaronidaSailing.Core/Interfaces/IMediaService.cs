using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public interface IMediaService
{
    Task<MediaImportResult> ImportAsync(
        Stream content,
        string originalFileName,
        MediaSource source = MediaSource.Upload,
        CancellationToken ct = default);
}
