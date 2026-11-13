using Microsoft.AspNetCore.Mvc;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Instagram;
using SaronidaSailing.Web.Models;

namespace SaronidaSailing.Web.ViewComponents;

public sealed class InstagramFeedViewComponent(
    IPageContentAccessor accessor,
    ICmsEditContext editContext,
    IInstagramFeedService feed) : ViewComponent
{
    private const int ThumbnailWidth = 480;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var isEditor = await editContext.IsEditorAsync();
        var content = await accessor.GetAsync();
        var stored = content.GetField(InstagramSection.PostCountKey);
        var count = InstagramPostCount.Parse(stored?.Type == ContentType.Text ? stored.Value : null);

        var posts = await feed.GetFeedAsync(InstagramFeedService.PostLimit, HttpContext.RequestAborted);
        if (posts.Count == 0 && !isEditor)
        {
            return Content(string.Empty);
        }

        var shown = isEditor ? posts : [.. posts.Take(count)];
        var tiles = shown
            .Select((post, index) =>
            {
                var source = post.Image.SourceAt(ThumbnailWidth) ?? post.Image.Sources[0];
                return new InstagramTileViewModel(
                    post.Permalink,
                    InstagramAltText.FromCaption(post.Caption),
                    AthensTime.ToAthens(post.PostedAt).Date,
                    source.Url,
                    post.Image.SrcSet,
                    post.Image.Width,
                    post.Image.Height,
                    index >= count);
            })
            .ToList();

        return View(new InstagramFeedViewModel(isEditor, count, tiles));
    }
}
