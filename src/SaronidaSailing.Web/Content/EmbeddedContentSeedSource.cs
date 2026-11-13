using System.Reflection;
using System.Text.Json;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Web.Content;

public sealed class EmbeddedContentSeedSource : IContentSeedSource
{
    private const string ResourcePrefix = "Seed/";

    private const string ResourceSuffix = ".json";

    private readonly Lazy<IReadOnlyList<ContentSeedGroup>> _groups;

    public EmbeddedContentSeedSource()
        : this(typeof(EmbeddedContentSeedSource).Assembly)
    {
    }

    public EmbeddedContentSeedSource(Assembly assembly) =>
        _groups = new Lazy<IReadOnlyList<ContentSeedGroup>>(() => Load(assembly));

    public IReadOnlyList<ContentSeedGroup> GetGroups() => _groups.Value;

    public static ContentSeedGroup Parse(string fileName, string json)
    {
        try
        {
            return ParseCore(fileName, json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Seed file '{fileName}' is invalid: {ex.Message}", ex);
        }
    }

    private static IReadOnlyList<ContentSeedGroup> Load(Assembly assembly)
    {
        var groups = new List<ContentSeedGroup>();
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                         && n.EndsWith(ResourceSuffix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            groups.Add(Parse(resource[ResourcePrefix.Length..], reader.ReadToEnd()));
        }

        return [.. groups.OrderBy(g => g.Name, StringComparer.Ordinal)];
    }

    private static ContentSeedGroup ParseCore(string fileName, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireObject(root, "the root");

        var name = "";
        var version = 0;
        var fields = new List<SeedField>();
        var lists = new List<SeedList>();
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "group":
                    name = RequireString(property.Value, "group");
                    break;
                case "version":
                    if (property.Value.ValueKind != JsonValueKind.Number
                        || !property.Value.TryGetInt32(out version)
                        || version < 1)
                    {
                        throw new InvalidOperationException("'version' must be a whole number of 1 or more.");
                    }

                    break;
                case "fields":
                    RequireObject(property.Value, "fields");
                    foreach (var field in property.Value.EnumerateObject())
                    {
                        fields.Add(ParseField(field.Name, field.Value));
                    }

                    break;
                case "lists":
                    RequireObject(property.Value, "lists");
                    foreach (var list in property.Value.EnumerateObject())
                    {
                        lists.Add(ParseList(list.Name, list.Value));
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unknown property '{property.Name}'.");
            }
        }

        if (name.Length == 0 || version == 0)
        {
            throw new InvalidOperationException("'group' and 'version' are required.");
        }

        var expectedName = fileName.EndsWith(ResourceSuffix, StringComparison.Ordinal)
            ? fileName[..^ResourceSuffix.Length]
            : fileName;
        if (name != expectedName)
        {
            throw new InvalidOperationException($"'group' is '{name}' but the file name says '{expectedName}'.");
        }

        foreach (var field in fields)
        {
            RequireInGroup(name, field.Key);
        }

        foreach (var list in lists)
        {
            RequireInGroup(name, list.ListKey);
        }

        return new ContentSeedGroup(name, version, fields, lists);
    }

    private static SeedField ParseField(string key, JsonElement element)
    {
        RequireObject(element, key);
        ContentType? type = null;
        string? url = null;
        var values = new Dictionary<string, string>();
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name == "type")
            {
                type = ParseType(RequireString(property.Value, $"{key}.type"), key);
            }
            else if (property.Name == "url")
            {
                url = RequireString(property.Value, $"{key}.url");
            }
            else if (ContentLocales.IsSupported(property.Name))
            {
                values[property.Name] = RequireString(property.Value, $"{key}.{property.Name}");
            }
            else
            {
                throw new InvalidOperationException($"'{key}' has unknown property '{property.Name}'.");
            }
        }

        if (type is null)
        {
            throw new InvalidOperationException($"'{key}' has no type.");
        }

        if (values.Count == 0)
        {
            throw new InvalidOperationException($"'{key}' has no value.");
        }

        if (type == ContentType.Link)
        {
            if (url is null)
            {
                throw new InvalidOperationException($"Link '{key}' needs a 'url' (the locale values are the labels).");
            }

            values = values.ToDictionary(v => v.Key, v => new ContentLink(url, v.Value).ToJson());
        }
        else if (url is not null)
        {
            throw new InvalidOperationException($"'{key}' is not a link and cannot have a 'url'.");
        }

        return new SeedField(key, type.Value, values);
    }

    private static SeedList ParseList(string listKey, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"List '{listKey}' must be an array.");
        }

        var items = new List<SeedListItem>();
        foreach (var item in element.EnumerateArray())
        {
            RequireObject(item, listKey);
            var fields = new List<SeedField>();
            foreach (var field in item.EnumerateObject())
            {
                if (!ContentKeys.TryParseField($"{listKey}.{field.Name}", out _, out _, out _))
                {
                    throw new InvalidOperationException($"'{listKey}.{field.Name}' is not a valid field key.");
                }

                fields.Add(ParseField(field.Name, field.Value));
            }

            items.Add(new SeedListItem(fields));
        }

        return new SeedList(listKey, items);
    }

    private static ContentType ParseType(string value, string key) => value switch
    {
        "text" => ContentType.Text,
        "richText" => ContentType.RichText,
        "link" => ContentType.Link,
        "image" => throw new InvalidOperationException($"'{key}': images cannot be seeded."),
        _ => throw new InvalidOperationException($"'{key}' has unknown type '{value}'.")
    };

    private static void RequireInGroup(string group, string key)
    {
        if (key != group && !key.StartsWith(group + ".", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Key '{key}' is outside the group '{group}'.");
        }
    }

    private static string RequireString(JsonElement element, string what) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : throw new InvalidOperationException($"'{what}' must be a string.");

    private static void RequireObject(JsonElement element, string what)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"'{what}' must be an object.");
        }
    }
}
