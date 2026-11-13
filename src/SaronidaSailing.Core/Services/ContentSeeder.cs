using System.Globalization;
using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class ContentSeeder(
    IContentSeedSource source,
    IContentService content,
    IContentItemRepository items,
    IContentListItemRepository listItems,
    IIntegrationSettingRepository settings,
    IUnitOfWork unitOfWork,
    IDistributedLock distributedLock,
    ILogger<ContentSeeder> logger) : IContentSeeder
{
    public const string LockResource = "saronida:content-seed";

    public const string EditorName = "seed";

    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(2);

    private static readonly ContentEditor Editor = new(EditorName, null);

    public static string MarkerKey(string groupName) => $"seed.{groupName}";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var groups = source.GetGroups();
        if (groups.Count == 0)
        {
            return;
        }

        await using var _ = await distributedLock.AcquireAsync(LockResource, LockTimeout, ct);
        foreach (var group in groups)
        {
            await SeedGroupAsync(group, ct);
        }
    }

    private async Task SeedGroupAsync(ContentSeedGroup group, CancellationToken ct)
    {
        var marker = MarkerKey(group.Name);
        var stored = await settings.GetAsync(marker, ct);
        if (int.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out var storedVersion)
            && storedVersion >= group.Version)
        {
            return;
        }

        var changes = new List<ContentChange>();
        var fieldValues = 0;
        var listCount = 0;

        foreach (var field in group.Fields)
        {
            fieldValues += await AddMissingValuesAsync(field, field.Key, null, changes, ct);
        }

        foreach (var list in group.Lists)
        {
            if (await ListHasItemsAsync(list.ListKey, ct))
            {
                continue;
            }

            listCount++;
            foreach (var item in list.Items)
            {
                var id = Guid.CreateVersion7();
                changes.Add(new AddListItem(list.ListKey, id));
                foreach (var field in item.Fields)
                {
                    fieldValues += await AddMissingValuesAsync(field, $"{list.ListKey}.{field.Key}", id, changes, ct);
                }
            }
        }

        if (changes.Count > 0)
        {
            var result = await content.ApplyChangesAsync(changes, Editor, ct);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Seed group '{group.Name}' v{group.Version} is invalid: {string.Join(" ", result.Errors)}");
            }
        }

        await settings.SetAsync(marker, group.Version.ToString(CultureInfo.InvariantCulture), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seeded content group {Group} v{Version}: {Fields} field value(s), {Lists} list(s).",
            group.Name, group.Version, fieldValues, listCount);
    }

    private async Task<int> AddMissingValuesAsync(
        SeedField field, string key, Guid? listItemId, List<ContentChange> changes, CancellationToken ct)
    {
        var added = 0;
        foreach (var (locale, value) in field.Values)
        {
            if (await ExistsAsync(key, locale, listItemId, ct))
            {
                continue;
            }

            changes.Add(new SetField(key, locale, field.Type, value, listItemId));
            added++;
        }

        return added;
    }

    private async Task<bool> ExistsAsync(string key, string locale, Guid? listItemId, CancellationToken ct)
    {
        if (listItemId is not null || !ContentKeys.TryParseField(key, out var page, out var section, out var name))
        {
            return false;
        }

        return await items.FindStandaloneAsync(page, section, name, locale, ct) is not null;
    }

    private async Task<bool> ListHasItemsAsync(string listKey, CancellationToken ct)
    {
        if (!ContentKeys.TryParseList(listKey, out var page, out var key))
        {
            return false;
        }

        return (await listItems.GetListAsync(page, key, ct)).Count > 0;
    }
}
