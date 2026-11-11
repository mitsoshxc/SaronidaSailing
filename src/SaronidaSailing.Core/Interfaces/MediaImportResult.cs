using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public enum MediaImportError
{
    TooLarge,
    NotAnImage,
    UnsupportedFormat,
    Heic,
    TooManyPixels,
    Busy,
}

public sealed record MediaImportResult(MediaAsset? Asset, bool AlreadyExisted, MediaImportError? Error)
{
    public static MediaImportResult Imported(MediaAsset asset) => new(asset, false, null);

    public static MediaImportResult Existing(MediaAsset asset) => new(asset, true, null);

    public static MediaImportResult Failed(MediaImportError error) => new(null, false, error);
}
