using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Pages.Errors;

[IgnoreAntiforgeryToken]
public abstract class ErrorPageModel(ILocalizedUrlService urlService) : PageModel
{
    public string Locale { get; private set; } = SiteCultures.Default;

    public string HomePath => urlService.GetPath("home", Locale);

    public string TripsPath => urlService.GetPath("trips", Locale);

    public string ContactPath => urlService.GetPath("contact", Locale);

    public void OnGet() => Prepare();

    public void OnPost() => Prepare();

    public void OnPut() => Prepare();

    public void OnPatch() => Prepare();

    public void OnDelete() => Prepare();

    protected abstract int Status { get; }

    protected abstract PathString? OriginalPath { get; }

    private void Prepare()
    {
        Locale = SiteCultures.ForPath(OriginalPath ?? default);
        Response.StatusCode = Status;
        Response.Headers.CacheControl = "no-store";
    }
}
