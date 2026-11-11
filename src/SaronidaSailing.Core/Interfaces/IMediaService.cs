namespace SaronidaSailing.Core.Interfaces;

public interface IMediaService
{
    Task<MediaImportResult> ImportAsync(Stream content, string originalFileName, CancellationToken ct = default);
}
