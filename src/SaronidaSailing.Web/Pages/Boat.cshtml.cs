using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Web.Caching;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Models;

namespace SaronidaSailing.Web.Pages;

[OutputCache(PolicyName = CacheTags.PolicyName, Tags = [CacheTags.BoatPage])]
public class BoatModel(IPageContentAccessor accessor) : PageModel
{
    public int PhotoCount { get; private set; }

    public async Task OnGetAsync() => PhotoCount = BoatGallery.CountPhotos(await accessor.GetAsync());
}
