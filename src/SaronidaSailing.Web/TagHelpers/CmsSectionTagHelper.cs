using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms-section")]
public sealed class CmsSectionTagHelper(IPageContentAccessor accessor, ICmsEditContext editContext) : TagHelper
{
    public override int Order => -100;

    [HtmlAttributeName("cms-section")]
    public string? Key { get; set; }

    [HtmlAttributeName("cms-section-label")]
    public string? Label { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var key = CmsFieldResolver.RequireSectionKey(Key, "cms-section");
        var label = CmsFieldResolver.RequireLabel(Label, $"Section '{key}'", "cms-section-label");
        var active = (await accessor.GetAsync()).IsSectionActive(key);

        if (!await editContext.IsEditorAsync())
        {
            if (!active)
            {
                output.SuppressOutput();
            }

            return;
        }

        output.Attributes.SetAttribute("data-cms-section", key);
        output.Attributes.SetAttribute("data-cms-section-label", label);
        if (!active)
        {
            output.Attributes.SetAttribute("data-cms-active", "false");
        }
    }
}
