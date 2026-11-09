using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement("a", Attributes = "cms-link")]
[HtmlTargetElement("a", Attributes = "cms-link-field")]
public sealed class CmsLinkTagHelper(IPageContentAccessor accessor, ILogger<CmsLinkTagHelper> logger) : TagHelper
{
    [HtmlAttributeName("cms-link")]
    public string? Key { get; set; }

    [HtmlAttributeName("cms-link-field")]
    public string? ItemField { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var value = await CmsFieldResolver.ReadAsync(context, accessor, Key, ItemField, "cms-link", "cms-link-field");
        var name = Key ?? ItemField;

        if (value is null)
        {
            output.SuppressOutput();
            return;
        }

        if (value.Type != ContentType.Link)
        {
            logger.LogWarning("Content '{Key}' is {Type} but the template renders it as a link.", name, value.Type);
            output.SuppressOutput();
            return;
        }

        if (!ContentLink.TryParse(value.Value, out var link))
        {
            logger.LogWarning("Content '{Key}' is not a valid link and was not rendered.", name);
            output.SuppressOutput();
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
