using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Caching;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.Pages;

[OutputCache(PolicyName = CacheTags.PolicyName, Tags = [CacheTags.ContactPage])]
public class ContactModel(IPageContentAccessor accessor, ICmsEditContext editContext) : PageModel
{
    public bool ShowMapNotice { get; private set; }

    public async Task OnGetAsync()
    {
        var url = (await accessor.GetAsync()).GetField(ContactMap.EmbedUrlKey)?.Value;
        var hasMap = !string.IsNullOrEmpty(url) && ContactMap.IsValidEmbedUrl(url);
        ShowMapNotice = hasMap || await editContext.IsEditorAsync();
    }
}
