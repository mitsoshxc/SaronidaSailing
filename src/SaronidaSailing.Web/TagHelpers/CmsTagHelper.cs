using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms")]
[HtmlTargetElement(Attributes = "cms-field")]
public sealed class CmsTagHelper(
    IPageContentAccessor accessor,
    ICmsEditContext editContext,
    ILogger<CmsTagHelper> logger) : TagHelper
{
    private const string RichAttribute = "cms-rich";

    [HtmlAttributeName("cms")]
    public string? Key { get; set; }

    [HtmlAttributeName("cms-field")]
    public string? ItemField { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var rich = context.AllAttributes.ContainsName(RichAttribute);
        output.Attributes.RemoveAll(RichAttribute);

        var templateType = rich ? ContentType.RichText : ContentType.Text;
        var field = await CmsFieldResolver.ReadAsync(context, accessor, Key, ItemField, "cms", "cms-field");
        var editor = await editContext.IsEditorAsync();
        var value = field.Value;

        if (value is not null && value.Type != templateType)
        {
            logger.LogWarning(
                "Content '{Key}' is {Type} but the template renders it as {Template}.",
                field.Key, value.Type, templateType);
            output.SuppressOutput();
            return;
        }

        var empty = value is null || string.IsNullOrWhiteSpace(value.Value);
        if (empty && !editor)
        {
            output.SuppressOutput();
            return;
        }

        if (editor)
        {
            output.Attributes.SetAttribute("data-cms-key", field.Key);
            output.Attributes.SetAttribute("data-cms-type", rich ? "richText" : "text");
            if (value is { IsFallback: true })
            {
                CmsFieldResolver.Flag(output, "data-cms-fallback");
            }

            if (rich && !string.IsNullOrWhiteSpace(value?.Value))
            {
                output.Attributes.SetAttribute("data-cms-value", value!.Value);
            }
        }

        if (empty)
        {
            CmsFieldResolver.Flag(output, "data-cms-empty");
            output.Content.SetContent(string.Empty);
            return;
        }

        if (rich)
        {
            output.Content.SetHtmlContent(value!.Value);
        }
        else
        {
            output.Content.SetContent(value!.Value);
        }
    }
}
