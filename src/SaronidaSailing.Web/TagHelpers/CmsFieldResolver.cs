using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

internal sealed record ResolvedField(string Key, ContentValue? Value);

internal static class CmsFieldResolver
{
    private sealed record ListScope(string ListKey, ContentListEntry Entry);

    private static readonly object ListItemKey = new();

    public static bool IsInsideList(TagHelperContext context) => context.Items.ContainsKey(ListItemKey);

    public static void EnterListItem(TagHelperContext context, string listKey, ContentListEntry entry) =>
        context.Items[ListItemKey] = new ListScope(listKey, entry);

    public static void LeaveListItem(TagHelperContext context) => context.Items.Remove(ListItemKey);

    public static void Flag(TagHelperOutput output, string name) =>
        output.Attributes.SetAttribute(new TagHelperAttribute(name));

    public static string RequireFieldKey(string? key, string attribute)
    {
        if (key is null || !ContentKeys.TryParseField(key, out _, out _, out _))
        {
            throw Invalid(attribute, key);
        }

        return key;
    }

    public static string RequireListKey(string? key, string attribute)
    {
        if (key is null || !ContentKeys.TryParseList(key, out _, out _))
        {
            throw Invalid(attribute, key);
        }

        return key;
    }

    public static string RequireAddLabel(string? label, string listKey, string attribute)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new InvalidOperationException($"List '{listKey}' requires a non-blank '{attribute}'.");
        }

        return label.Trim();
    }

    public static string RequireSectionKey(string? key, string attribute)
    {
        if (key is null || !ContentKeys.TryParseSection(key, out _, out _))
        {
            throw Invalid(attribute, key);
        }

        return key;
    }

    public static string RequireItemFieldKey(string? key, string attribute)
    {
        if (key is null || !ContentKeys.IsValidSegment(key))
        {
            throw Invalid(attribute, key);
        }

        return key;
    }

    public static async Task<ResolvedField> ReadAsync(
        TagHelperContext context,
        IPageContentAccessor accessor,
        string? fieldKey,
        string? itemFieldKey,
        string fieldAttribute,
        string itemFieldAttribute)
    {
        if (fieldKey is not null && itemFieldKey is not null)
        {
            throw new InvalidOperationException(
                $"An element cannot have both '{fieldAttribute}' and '{itemFieldAttribute}'.");
        }

        if (fieldKey is not null)
        {
            var key = RequireFieldKey(fieldKey, fieldAttribute);
            return new ResolvedField(key, (await accessor.GetAsync()).GetField(key));
        }

        var field = RequireItemFieldKey(itemFieldKey, itemFieldAttribute);
        if (!context.Items.TryGetValue(ListItemKey, out var item) || item is not ListScope scope)
        {
            throw new InvalidOperationException($"'{itemFieldAttribute}' must be inside an element with 'cms-list'.");
        }

        return new ResolvedField($"{scope.ListKey}.{field}", scope.Entry.Fields.GetValueOrDefault(field));
    }

    private static InvalidOperationException Invalid(string attribute, string? key) =>
        new($"'{key}' is not a valid key for '{attribute}'.");
}
