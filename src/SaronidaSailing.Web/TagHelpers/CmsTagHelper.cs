using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms")]
[HtmlTargetElement(Attributes = "cms-field")]
public sealed class CmsTagHelper(IPageContentAccessor accessor, ILogger<CmsTagHelper> logger) : TagHelper
{
    [HtmlAttributeName("cms")]
    public string? Key { get; set; }

    [HtmlAttributeName("cms-field")]
    public string? ItemField { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var value = await CmsFieldResolver.ReadAsync(context, accessor, Key, ItemField, "cms", "cms-field");

        if (value is null)
        {
            output.SuppressOutput();
            return;
        }

        switch (value.Type)
        {
            case ContentType.Text:
                output.Content.SetContent(value.Value);
                break;
            case ContentType.RichText:
                output.Content.SetHtmlContent(value.Value);
                break;
            default:
                logger.LogWarning(
                    "Content '{Key}' is {Type} but the template renders it as text.", Key ?? ItemField, value.Type);
                output.SuppressOutput();
                break;
        }
    }
}
