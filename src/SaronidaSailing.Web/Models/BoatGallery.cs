using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Models;

public static class BoatGallery
{
    public const string CoverKey = "boat.gallery.cover";

    public const string PhotosKey = "boat.gallery-photos";

    public const string PhotoImageField = "image";

    public static int CountPhotos(PageContent content)
    {
        var count = HasImage(content, content.GetField(CoverKey)) ? 1 : 0;
        foreach (var entry in content.GetList(PhotosKey).Where(e => e.IsActive))
        {
            if (HasImage(content, entry.Fields.GetValueOrDefault(PhotoImageField)))
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasImage(PageContent content, ContentValue? value) =>
        value is { Type: ContentType.Image }
        && Guid.TryParse(value.Value, out var id)
        && content.GetImage(id) is { Sources.Count: > 0 };
}
