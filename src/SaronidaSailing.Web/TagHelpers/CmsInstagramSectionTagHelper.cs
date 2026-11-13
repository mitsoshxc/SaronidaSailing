using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms-instagram-count")]
public sealed class CmsInstagramSectionTagHelper(ICmsEditContext editContext) : TagHelper
{
    [HtmlAttributeName("cms-instagram-count")]
    public int Count { get; set; }

    [HtmlAttributeName("cms-instagram-empty")]
    public bool Empty { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!await editContext.IsEditorAsync())
        {
            return;
        }

        output.Attributes.SetAttribute("data-cms-instagram-count", Count);
        output.Attributes.SetAttribute("data-cms-instagram-count-key", InstagramSection.PostCountKey);
        if (Empty)
        {
            CmsFieldResolver.Flag(output, "data-cms-empty");
        }
    }
}
