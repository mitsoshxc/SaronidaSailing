using System.Text.RegularExpressions;
using Ganss.Xss;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Infrastructure.Content;

public sealed partial class RichTextSanitizer : IRichTextSanitizer
{
    private static readonly string[] AllowedTags = ["p", "br", "strong", "em", "a", "ul", "ol", "li", "h3"];

    private static readonly HashSet<string> DroppedWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "frame", "frameset", "object", "embed", "applet", "noscript", "template",
        "textarea", "title", "svg", "math"
    };

    private readonly HtmlSanitizer _sanitizer = Create();

    public string Sanitize(string html)
    {
        var result = _sanitizer.Sanitize(html).Trim();
        return string.IsNullOrWhiteSpace(EmptyParagraph().Replace(result, "")) ? "" : result;
    }

    private static HtmlSanitizer Create()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(AllowedTags);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedClasses.Clear();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["https", "mailto", "tel"]);
        sanitizer.AllowDataAttributes = false;
        sanitizer.KeepChildNodes = true;
        sanitizer.RemovingTag += (_, e) =>
        {
            if (DroppedWithContent.Contains(e.Tag.NodeName))
            {
                e.Tag.TextContent = "";
            }
        };
        sanitizer.FilterUrl += (_, e) =>
        {
            if (!string.Equals(e.Tag.NodeName, "a", StringComparison.OrdinalIgnoreCase)
                || !ContentLink.IsAllowedUrl(e.OriginalUrl))
            {
                e.SanitizedUrl = null;
            }
        };
        return sanitizer;
    }

    [GeneratedRegex(@"<p>(?:\s|<br\s*/?>)*</p>", RegexOptions.IgnoreCase)]
    private static partial Regex EmptyParagraph();
}
