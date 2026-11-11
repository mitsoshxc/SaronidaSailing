using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement(Attributes = "cms-list")]
public sealed class CmsListTagHelper(IPageContentAccessor accessor, ICmsEditContext editContext) : TagHelper
{
    [HtmlAttributeName("cms-list")]
    public string? Key { get; set; }

    [HtmlAttributeName("cms-add-label")]
    public string? AddLabel { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var key = CmsFieldResolver.RequireListKey(Key, "cms-list");
        var addLabel = CmsFieldResolver.RequireLabel(AddLabel, $"List '{key}'", "cms-add-label");
        if (CmsFieldResolver.IsInsideList(context))
        {
            throw new InvalidOperationException($"'cms-list' cannot be nested (list '{key}').");
        }

        var editor = await editContext.IsEditorAsync();
        var entries = (await accessor.GetAsync()).GetList(key)
            .Where(e => editor || e.IsActive)
            .OrderBy(e => e.SortOrder)
            .ToList();

        if (entries.Count == 0 && !editor)
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
                copies.AppendHtml(await RenderCopyAsync(context, output, attributes, key, entry, editor));
            }

            if (editor)
            {
                var empty = new ContentListEntry(Guid.Empty, 0, true, new Dictionary<string, ContentValue>());
                copies.AppendHtml("<template data-cms-list-template=\"");
                copies.Append(key);
                copies.AppendHtml("\" data-cms-add-label=\"");
                copies.Append(addLabel);
                copies.AppendHtml("\">");
                copies.AppendHtml(await RenderCopyAsync(context, output, attributes, key, empty, editor));
                copies.AppendHtml("</template>");
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

    private static async Task<TagHelperOutput> RenderCopyAsync(
        TagHelperContext context,
        TagHelperOutput output,
        TagHelperAttributeList attributes,
        string key,
        ContentListEntry entry,
        bool editor)
    {
        CmsFieldResolver.EnterListItem(context, key, entry);
        var children = await output.GetChildContentAsync(useCachedResult: false);

        var copyAttributes = new TagHelperAttributeList(attributes);
        if (editor)
        {
            copyAttributes.SetAttribute("data-cms-list", key);
            if (entry.Id != Guid.Empty)
            {
                copyAttributes.SetAttribute("data-cms-item", entry.Id.ToString());
            }

            if (!entry.IsActive)
            {
                copyAttributes.SetAttribute("data-cms-active", "false");
            }
        }

        var copy = new TagHelperOutput(
            output.TagName,
            copyAttributes,
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()))
        {
            TagMode = output.TagMode,
        };
        copy.Content.SetHtmlContent(children);
        return copy;
    }
}
