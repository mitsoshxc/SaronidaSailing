using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Identity;
using SaronidaSailing.Web.Instagram;

namespace SaronidaSailing.Web.Pages.Admin;

[Authorize(Policy = AdminPolicy.Name)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InstagramModel(
    IInstagramConnectionService connection,
    IInstagramApiClient client,
    IInstagramFeedService feed,
    IInstagramJobQueue jobQueue,
    IAuditLog audit,
    InstagramOAuthState oauthState,
    IOptions<SiteOptions> site) : AdminPageModel
{
    public const string NotSetUpText = "Instagram is not set up on this server (the Meta app ID or secret is missing).";

    private static readonly Dictionary<string, (string Text, bool IsError)> Banners = new()
    {
        ["connected"] = ("Instagram connected.", false),
        ["denied"] = ("Instagram connection was cancelled.", true),
        ["expired"] = ("The connection attempt took too long. Please try again.", true),
        ["invalid"] = ("The connection attempt could not be verified. Please try again.", true),
        ["failed"] = ("Instagram did not accept the connection. Please try again later.", true),
        ["notconfigured"] = (NotSetUpText, true),
        ["fetch-queued"] = ("Fetching the latest posts. Reload this page in a minute to see the result.", false),
    };

    private static readonly Dictionary<string, string> FailureTexts = new()
    {
        ["failed:TokenInvalid"] = "Instagram rejected the access, please reconnect",
        ["failed:Transient"] = "Instagram was not reachable, will retry at the next scheduled run",
        ["failed:Other"] = "Instagram returned an error",
        ["skipped:not-connected"] = "skipped, not connected",
    };

    private static readonly Dictionary<string, string> FetchTexts = new(FailureTexts)
    {
        ["failed:Transient"] = "Instagram was not reachable, will retry at the next scheduled fetch",
        ["skipped:no-posts"] = "Instagram returned no posts; the last posts were kept",
    };

    private static readonly Dictionary<string, string> RefreshTexts = new(FailureTexts)
    {
        ["ok"] = "renewed",
        ["skipped:too-recent"] = "not needed yet",
    };

    public InstagramStatus Status { get; private set; } = null!;

    public InstagramActivity Activity { get; private set; } = null!;

    public string? BannerText { get; private set; }

    public bool BannerIsError { get; private set; }

    public string? ExpiresAtText => Status.ExpiresAt is { } value ? AthensTime.Format(value) : null;

    public string? ConnectedAtText => Status.ConnectedAt is { } value ? AthensTime.Format(value) : null;

    public string? LastFetchText => Describe(Activity.LastFetchAt, Activity.LastFetchResult, FetchTexts);

    public string? LastRefreshText => Describe(Activity.LastRefreshAt, Activity.LastRefreshResult, RefreshTexts);

    public async Task OnGetAsync(string? result, CancellationToken ct)
    {
        Status = await connection.GetStatusAsync(ct);
        Activity = await feed.GetActivityAsync(ct);
        if (result is not null && Banners.TryGetValue(result, out var banner))
        {
            (BannerText, BannerIsError) = banner;
        }
    }

    public IActionResult OnPostConnect()
    {
        if (!client.IsConfigured)
        {
            return LocalRedirect(InstagramPaths.ResultUrl("notconfigured"));
        }

        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminUserId))
        {
            return Forbid();
        }

        var state = oauthState.Create(HttpContext, adminUserId);
        return Redirect(client.BuildAuthorizeUrl(InstagramPaths.RedirectUri(site.Value.BaseUrl), state));
    }

    public async Task<IActionResult> OnPostFetchAsync(CancellationToken ct)
    {
        if ((await connection.GetStatusAsync(ct)).State != InstagramConnectionState.Connected)
        {
            return LocalRedirect(InstagramPaths.Admin);
        }

        jobQueue.EnqueueFetch();
        audit.Record("instagram-fetch-requested", User.Identity?.Name, ClientIp);
        return LocalRedirect(InstagramPaths.ResultUrl("fetch-queued"));
    }

    public async Task<IActionResult> OnPostDisconnectAsync(CancellationToken ct)
    {
        await connection.DisconnectAsync(User.Identity?.Name ?? "", ClientIp, ct);
        return LocalRedirect(InstagramPaths.Admin);
    }

    private static string? Describe(DateTime? at, string? result, Dictionary<string, string> texts)
    {
        if (at is null || result is null)
        {
            return null;
        }

        return $"{AthensTime.Format(at.Value)} — {ResultText(result, texts)}";
    }

    private static string ResultText(string result, Dictionary<string, string> texts)
    {
        if (result.StartsWith("ok:", StringComparison.Ordinal) && int.TryParse(result.AsSpan(3), out var count))
        {
            return count == 1 ? "1 post" : $"{count} posts";
        }

        return texts.GetValueOrDefault(result, result);
    }
}
