using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms-section-link")]
public sealed class CmsSectionLinkTagHelper(IPageContentAccessor accessor, ICmsEditContext editContext) : TagHelper
{
    public override int Order => -100;

    [HtmlAttributeName("cms-section-link")]
    public string? Key { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var key = CmsFieldResolver.RequireSectionKey(Key, "cms-section-link");
        var content = await accessor.GetAsync();

        ContentKeys.TryParseSection(key, out var pageKey, out _);
        if (pageKey != content.PageKey && pageKey != ContentKeys.Shared)
        {
            throw new InvalidOperationException(
                $"'cms-section-link' can only point to a section of the current page ('{content.PageKey}') or 'shared' (got '{key}').");
        }

        output.Attributes.RemoveAll("cms-section-link");
        var active = content.IsSectionActive(key);

        if (!await editContext.IsEditorAsync())
        {
            if (!active)
            {
                output.SuppressOutput();
            }

            return;
        }

        output.Attributes.SetAttribute("data-cms-section-link", key);
        if (!active)
        {
            output.Attributes.SetAttribute("data-cms-active", "false");
        }
    }
}
