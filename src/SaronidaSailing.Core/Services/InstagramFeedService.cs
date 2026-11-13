using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class InstagramFeedService(
    IInstagramApiClient client,
    IInstagramConnectionService connection,
    IInstagramPostRepository posts,
    IMediaAssetRepository assets,
    IIntegrationSettingRepository settings,
    IMediaService media,
    IUnitOfWork unitOfWork,
    IPageCacheInvalidator pageCache,
    ILogger<InstagramFeedService> logger,
    TimeProvider time) : IInstagramFeedService
{
    public const int PostLimit = 12;

    public async Task<InstagramFetchResult> FetchAsync(CancellationToken ct = default)
    {
        var token = await connection.GetAccessTokenAsync(ct);
        if (token is null)
        {
            return await FinishWithoutChangesAsync("skipped:not-connected", ct);
        }

        IReadOnlyList<InstagramMediaItem> items;
        try
        {
            items = await client.GetRecentMediaAsync(token, PostLimit, ct);
        }
        catch (InstagramApiException ex)
        {
            logger.LogWarning("Instagram fetch failed: kind={Kind} code={Code}.", ex.Kind, ex.ErrorCode);
            if (ex.Kind == InstagramApiErrorKind.TokenInvalid)
            {
                await connection.MarkTokenInvalidAsync(ct);
            }

            return await FinishWithoutChangesAsync($"failed:{ex.Kind}", ct);
        }

        if (items.Count == 0)
        {
            logger.LogWarning("Instagram returned no posts; the stored posts were kept.");
            return await FinishWithoutChangesAsync("skipped:no-posts", ct);
        }

        var stored = await posts.ListAllAsync(ct);
        var storedById = stored.ToDictionary(x => x.InstagramId);
        var latest = items.DistinctBy(x => x.Id).ToList();
        var latestIds = latest.Select(x => x.Id).ToHashSet();

        var assetIds = new Dictionary<string, Guid>();
        var skipped = 0;
        foreach (var item in latest.Where(x => !storedById.ContainsKey(x.Id)))
        {
            var assetId = await ImportThumbnailAsync(item, ct);
            if (assetId is { } id)
            {
                assetIds[item.Id] = id;
            }
            else
            {
                skipped++;
            }
        }

        var added = 0;
        var updated = 0;
        foreach (var item in latest)
        {
            if (storedById.TryGetValue(item.Id, out var existing))
            {
                if (existing.Update(item.Caption, item.Permalink))
                {
                    updated++;
                }
            }
            else if (assetIds.TryGetValue(item.Id, out var assetId))
            {
                posts.Add(InstagramPost.Create(item.Id, item.Permalink, item.Caption, item.MediaType, assetId, item.PostedAt.ToUniversalTime()));
                added++;
            }
        }

        var removed = 0;
        foreach (var gone in stored.Where(x => !latestIds.Contains(x.InstagramId)))
        {
            posts.Remove(gone);
            removed++;
        }

        var code = $"ok:{stored.Count + added - removed}";
        await StageStatusAsync(code, ct);
        await unitOfWork.SaveChangesAsync(ct);
        if (added + updated + removed > 0)
        {
            await pageCache.EvictPagesAsync(["home"], ct);
        }

        logger.LogInformation(
            "Instagram fetch finished: added={Added} updated={Updated} removed={Removed} skipped={Skipped}.",
            added, updated, removed, skipped);
        return new InstagramFetchResult(code);
    }

    public async Task<InstagramActivity> GetActivityAsync(CancellationToken ct = default) =>
        new(
            await posts.CountAsync(ct),
            InstagramTimestamps.Parse(await settings.GetAsync(InstagramSettingKeys.LastFetchAt, ct)),
            await settings.GetAsync(InstagramSettingKeys.LastFetchResult, ct),
            InstagramTimestamps.Parse(await settings.GetAsync(InstagramSettingKeys.LastRefreshAt, ct)),
            await settings.GetAsync(InstagramSettingKeys.LastRefreshResult, ct));

    public async Task<IReadOnlyList<InstagramFeedPost>> GetFeedAsync(int take, CancellationToken ct = default)
    {
        if (take <= 0)
        {
            return [];
        }

        var stored = await posts.ListNewestFirstAsync(PostLimit, ct);
        if (stored.Count == 0)
        {
            return [];
        }

        var images = (await assets.GetByIdsAsync([.. stored.Select(p => p.MediaAssetId).Distinct()], ct))
            .ToDictionary(a => a.Id, PageImage.From);

        return [.. stored
            .Where(p => IsHttpsUrl(p.Permalink) && images.TryGetValue(p.MediaAssetId, out var image) && image.Sources.Count > 0)
            .Select(p => new InstagramFeedPost(p.Permalink, p.Caption, p.PostedAt, images[p.MediaAssetId]))
            .Take(take)];
    }

    private static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private async Task<Guid?> ImportThumbnailAsync(InstagramMediaItem item, CancellationToken ct)
    {
        try
        {
            await using var image = await client.DownloadImageAsync(item.ImageUrl, ct);
            var result = await media.ImportAsync(image, $"instagram-{item.Id}.jpg", MediaSource.Instagram, ct);
            if (result.Asset is { } asset)
            {
                return asset.Id;
            }

            logger.LogWarning("Instagram post {PostId} skipped: the thumbnail was rejected ({Error}).", item.Id, result.Error);
            return null;
        }
        catch (InstagramApiException ex)
        {
            logger.LogWarning(
                "Instagram post {PostId} skipped: the thumbnail download failed (kind={Kind} code={Code}).",
                item.Id, ex.Kind, ex.ErrorCode);
            return null;
        }
    }

    private async Task<InstagramFetchResult> FinishWithoutChangesAsync(string code, CancellationToken ct)
    {
        await StageStatusAsync(code, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new InstagramFetchResult(code);
    }

    private async Task StageStatusAsync(string code, CancellationToken ct)
    {
        await settings.SetAsync(InstagramSettingKeys.LastFetchAt, InstagramTimestamps.Format(time.GetUtcNow().UtcDateTime), ct);
        await settings.SetAsync(InstagramSettingKeys.LastFetchResult, code, ct);
    }
}
