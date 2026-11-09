using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

internal static class CmsFieldResolver
{
    private static readonly object ListItemKey = new();

    public static bool IsInsideList(TagHelperContext context) => context.Items.ContainsKey(ListItemKey);

    public static void EnterListItem(TagHelperContext context, ContentListEntry entry) =>
        context.Items[ListItemKey] = entry;

    public static void LeaveListItem(TagHelperContext context) => context.Items.Remove(ListItemKey);

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

    public static async Task<ContentValue?> ReadAsync(
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
            return (await accessor.GetAsync()).GetField(key);
        }

        var field = RequireItemFieldKey(itemFieldKey, itemFieldAttribute);
        if (!context.Items.TryGetValue(ListItemKey, out var item) || item is not ContentListEntry entry)
        {
            throw new InvalidOperationException($"'{itemFieldAttribute}' must be inside an element with 'cms-list'.");
        }

        return entry.Fields.GetValueOrDefault(field);
    }

    private static InvalidOperationException Invalid(string attribute, string? key) =>
        new($"'{key}' is not a valid key for '{attribute}'.");
}
