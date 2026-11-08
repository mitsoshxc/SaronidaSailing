using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SaronidaSailing.Web.Localization;

public sealed class LocalizedPageRouteConvention : IPageRouteModelConvention
{
    public void Apply(PageRouteModel model)
    {
        var page = SitePages.FindByPagePath(model.ViewEnginePath);
        if (page is null)
        {
            return;
        }

        model.Selectors.Clear();
        model.Selectors.Add(CreateSelector(page.GreekSlug));
        model.Selectors.Add(CreateSelector(page.EnglishSlug.Length == 0
            ? SiteCultures.EnglishPrefix
            : $"{SiteCultures.EnglishPrefix}/{page.EnglishSlug}"));
    }

    private static SelectorModel CreateSelector(string template) => new()
    {
        AttributeRouteModel = new AttributeRouteModel { Template = template },
    };
}
