using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement("cms-json-ld", TagStructure = TagStructure.WithoutEndTag)]
public sealed partial class CmsJsonLdTagHelper(
    IPageContentAccessor accessor,
    ILocalizedUrlService urlService,
    IOptions<SiteOptions> site) : TagHelper
{
    private const string BusinessName = "Saronida Sailing";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (urlService.GetCurrentPage() is null)
        {
            output.SuppressOutput();
            return;
        }

        var content = await accessor.GetAsync();
        var business = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "LocalBusiness",
            ["name"] = BusinessName,
            ["url"] = site.Value.BaseUrl + urlService.GetPath("home", content.Locale),
        };

        if (ReadText(content, "shared.footer.tagline") is { } description)
        {
            business["description"] = description;
        }

        var phones = new[] { "shared.contact.phone", "shared.contact.phone-2" }
            .Select(key => ReadLinkTarget(content, key, "tel:"))
            .OfType<string>()
            .Where(IsPhone)
            .ToList();
        if (phones.Count == 1)
        {
            business["telephone"] = phones[0];
        }
        else if (phones.Count > 1)
        {
            business["telephone"] = new JsonArray([.. phones.Select(p => JsonValue.Create(p))]);
        }

        if (ReadLinkTarget(content, "shared.contact.email", "mailto:") is { } email && IsEmail(email))
        {
            business["email"] = email;
        }

        if (ReadText(content, "shared.contact.address") is { } street)
        {
            business["address"] = new JsonObject
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = street,
                ["addressCountry"] = "GR",
            };
        }

        var profiles = new[] { "shared.social.instagram", "shared.social.facebook" }
            .Select(key => ReadLinkTarget(content, key, "https:"))
            .OfType<string>()
            .ToList();
        if (profiles.Count > 0)
        {
            business["sameAs"] = new JsonArray([.. profiles.Select(p => JsonValue.Create(p))]);
        }

        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("type", new HtmlString("application/ld+json"));
        output.Content.SetHtmlContent(business.ToJsonString());
    }

    private static string? ReadText(PageContent content, string key)
    {
        var value = content.GetField(key);
        return value is { Type: ContentType.Text } && !IsEmptyOrPlaceholder(value.Value) ? value.Value.Trim() : null;
    }

    private static string? ReadLinkTarget(PageContent content, string key, string scheme)
    {
        var value = content.GetField(key);
        if (value is not { Type: ContentType.Link } || !ContentLink.TryParse(value.Value, out var link)
            || !link.Url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var target = link.Url[scheme.Length..];
        if (scheme == "https:")
        {
            target = link.Url;
        }
        else if (scheme == "mailto:" && target.IndexOf('?') is var query and >= 0)
        {
            target = target[..query];
        }

        return IsEmptyOrPlaceholder(target) ? null : target.Trim();
    }

    private static bool IsEmptyOrPlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var open = value.IndexOf('[');
        if (open >= 0 && value.IndexOf(']', open) > open)
        {
            return true;
        }

        return value.Contains("example.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPhone(string value) => PhonePattern().IsMatch(value);

    private static bool IsEmail(string value) => EmailPattern().IsMatch(value);

    [GeneratedRegex(@"^\+?[0-9 ()\-]{5,}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
