using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms-section")]
public sealed class CmsSectionTagHelper(IPageContentAccessor accessor) : TagHelper
{
    public override int Order => -100;

    [HtmlAttributeName("cms-section")]
    public string? Key { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var key = CmsFieldResolver.RequireSectionKey(Key, "cms-section");

        if (!(await accessor.GetAsync()).IsSectionActive(key))
        {
            output.SuppressOutput();
        }
    }
}
