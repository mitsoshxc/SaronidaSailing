using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms-link-label")]
public sealed class CmsLinkLabelTagHelper(ICmsEditContext editContext) : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!CmsFieldResolver.TryGetLinkLabel(context, out var label))
        {
            throw new InvalidOperationException(
                "'cms-link-label' must be inside an <a> with 'cms-keep-content' and 'cms-link' or 'cms-link-field'.");
        }

        output.Attributes.RemoveAll("cms-link-label");
        output.Content.SetContent(label);

        if (await editContext.IsEditorAsync())
        {
            CmsFieldResolver.Flag(output, "data-cms-link-label");
        }
    }
}
