using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement("a", Attributes = "cms-link")]
[HtmlTargetElement("a", Attributes = "cms-link-field")]
public sealed class CmsLinkTagHelper(
    IPageContentAccessor accessor,
    ICmsEditContext editContext,
    ILogger<CmsLinkTagHelper> logger) : TagHelper
{
    [HtmlAttributeName("cms-link")]
    public string? Key { get; set; }

    [HtmlAttributeName("cms-link-field")]
    public string? ItemField { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var field = await CmsFieldResolver.ReadAsync(context, accessor, Key, ItemField, "cms-link", "cms-link-field");
        var editor = await editContext.IsEditorAsync();
        var value = field.Value;

        if (value is not null && value.Type != ContentType.Link)
        {
            logger.LogWarning("Content '{Key}' is {Type} but the template renders it as a link.", field.Key, value.Type);
            output.SuppressOutput();
            return;
        }

        var blank = value is null || string.IsNullOrWhiteSpace(value.Value);
        if (blank && !editor)
        {
            output.SuppressOutput();
            return;
        }

        ContentLink? link = null;
        var invalid = false;
        if (!blank && !ContentLink.TryParse(value!.Value, out link))
        {
            logger.LogWarning("Content '{Key}' is not a valid link and was not rendered.", field.Key);
            if (!editor)
            {
                output.SuppressOutput();
                return;
            }

            invalid = true;
        }

        if (editor)
        {
            output.Attributes.SetAttribute("data-cms-key", field.Key);
            output.Attributes.SetAttribute("data-cms-type", "link");
            if (value is { IsFallback: true })
            {
                CmsFieldResolver.Flag(output, "data-cms-fallback");
            }

            if (link is not null)
            {
                output.Attributes.SetAttribute("data-cms-value", value!.Value);
            }
        }

        if (link is null)
        {
            output.Attributes.RemoveAll("href");
            output.Attributes.RemoveAll("target");
            output.Attributes.RemoveAll("rel");
            CmsFieldResolver.Flag(output, invalid ? "data-cms-invalid" : "data-cms-empty");
            output.Content.SetContent(string.Empty);
            return;
        }

        output.Attributes.SetAttribute("href", link.Url);
        output.Content.SetContent(link.Label);

        if (link.Url.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            var locale = (await accessor.GetAsync()).Locale;
            output.Attributes.SetAttribute("target", "_blank");
            output.Attributes.SetAttribute("rel", "noopener");
            output.Content.AppendHtml("<span class=\"visually-hidden\">");
            output.Content.Append(UiText.OpensInNewTab(locale));
            output.Content.AppendHtml("</span>");
        }
    }
}
