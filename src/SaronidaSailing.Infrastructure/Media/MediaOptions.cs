namespace SaronidaSailing.Infrastructure.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public string RootPath { get; set; } = "";

    public string OriginalsPath { get; set; } = "";
}
