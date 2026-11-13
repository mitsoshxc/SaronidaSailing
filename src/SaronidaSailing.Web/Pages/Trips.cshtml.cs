using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Web.Caching;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Models;

namespace SaronidaSailing.Web.Pages;

[OutputCache(PolicyName = CacheTags.TripsPolicyName, Tags = [CacheTags.TripsPage])]
public class TripsModel(IPageContentAccessor accessor, ICmsEditContext editContext) : PageModel
{
    public IReadOnlyList<TripTab> Tabs { get; private set; } = [];

    public string? SelectedTrip { get; private set; }

    public async Task OnGetAsync()
    {
        var content = await accessor.GetAsync();
        var editor = await editContext.IsEditorAsync();
        bool IsShown(TripTab tab) => editor || content.IsSectionActive(tab.SectionKey);

        Tabs = TripTabs.All.Where(IsShown).ToList();
        SelectedTrip = TripTabs.Resolve(Request.Query[TripTabs.QueryKey].ToString(), IsShown);
    }
}
