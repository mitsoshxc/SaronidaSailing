using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms-list")]
public sealed class CmsListTagHelper(IPageContentAccessor accessor) : TagHelper
{
    [HtmlAttributeName("cms-list")]
    public string? Key { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var key = CmsFieldResolver.RequireListKey(Key, "cms-list");
        if (CmsFieldResolver.IsInsideList(context))
        {
            throw new InvalidOperationException($"'cms-list' cannot be nested (list '{key}').");
        }

        var entries = (await accessor.GetAsync()).GetList(key)
            .Where(e => e.IsActive)
            .OrderBy(e => e.SortOrder)
            .ToList();

        if (entries.Count == 0)
        {
            output.SuppressOutput();
            return;
        }

        var attributes = new TagHelperAttributeList(output.Attributes);
        attributes.RemoveAll("cms-list");

        var copies = new HtmlContentBuilder();
        try
        {
            foreach (var entry in entries)
            {
                CmsFieldResolver.EnterListItem(context, entry);
                var children = await output.GetChildContentAsync(useCachedResult: false);

                var copy = new TagHelperOutput(
                    output.TagName,
                    new TagHelperAttributeList(attributes),
                    (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()))
                {
                    TagMode = output.TagMode,
                };
                copy.Content.SetHtmlContent(children);
                copies.AppendHtml(copy);
            }
        }
        finally
        {
            CmsFieldResolver.LeaveListItem(context);
        }

        output.TagName = null;
        output.Attributes.Clear();
        output.Content.SetHtmlContent(copies);
    }
}
