namespace SaronidaSailing.Web.Models;

public sealed record InstagramTileViewModel(
    string Permalink,
    string? CaptionAlt,
    DateTime PostedOn,
    string Src,
    string SrcSet,
    int Width,
    int Height,
    bool Hidden);

public sealed record InstagramFeedViewModel(
    bool IsEditor,
    int Count,
    IReadOnlyList<InstagramTileViewModel> Tiles);
