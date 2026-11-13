using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Web.Caching;

namespace SaronidaSailing.Web.Pages;

[OutputCache(PolicyName = CacheTags.PolicyName, Tags = [CacheTags.TripsPage])]
public class TripsModel : PageModel
{
}
