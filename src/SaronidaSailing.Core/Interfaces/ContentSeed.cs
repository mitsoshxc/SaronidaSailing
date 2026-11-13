using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Core.Interfaces;

public sealed record SeedField(string Key, ContentType Type, IReadOnlyDictionary<string, string> Values);

public sealed record SeedListItem(IReadOnlyList<SeedField> Fields);

public sealed record SeedList(string ListKey, IReadOnlyList<SeedListItem> Items);

public sealed record ContentSeedGroup(
    string Name, int Version, IReadOnlyList<SeedField> Fields, IReadOnlyList<SeedList> Lists);
