using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Identity;
using SaronidaSailing.Web.Instagram;

namespace SaronidaSailing.Web.Pages.Admin;

[Authorize(Policy = AdminPolicy.Name)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InstagramModel(
    IInstagramConnectionService connection,
    IInstagramAppSettingsService appSettings,
    IInstagramApiClient client,
    IInstagramFeedService feed,
    IInstagramScheduleSync schedule,
    IInstagramJobQueue jobQueue,
    IAuditLog audit,
    InstagramOAuthState oauthState,
    IOptions<SiteOptions> site) : AdminPageModel
{
    public const string NotSetUpBeforeLink = "Instagram is not set up yet: enter the Meta App ID and App secret under ";

    public const string NotSetUpLinkText = "Meta app";

    public const string AppIdFieldId = "MetaAppId";

    public const string AppSecretFieldId = "MetaAppSecret";

    private static readonly Dictionary<string, (string Text, bool IsError)> Banners = new()
    {
        ["connected"] = ("Instagram connected.", false),
        ["denied"] = ("Instagram connection was cancelled.", true),
        ["expired"] = ("The connection attempt took too long. Please try again.", true),
        ["invalid"] = ("The connection attempt could not be verified. Please try again.", true),
        ["failed"] = ("Instagram did not accept the connection. Please try again later.", true),
        ["app-saved"] = ("Meta app settings saved.", false),
        ["app-saved-disconnected"] = ("Meta app settings saved. Instagram was disconnected; connect again.", false),
        ["app-removed"] = ("Meta app settings removed.", false),
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

    [BindProperty]
    public string? AppId { get; set; }

    [BindProperty(Name = "AppSecret")]
    public string? NewSecret { get; set; }

    [BindProperty]
    public bool ConfirmDisconnect { get; set; }

    public InstagramStatus Status { get; private set; } = null!;

    public InstagramAppStatus AppStatus { get; private set; } = null!;

    public InstagramActivity Activity { get; private set; } = null!;

    public bool SchedulesOn { get; private set; }

    public string? BannerText { get; private set; }

    public bool BannerIsError { get; private set; }

    public bool BannerIsNotSetUp { get; private set; }

    public string? AppError { get; private set; }

    public bool AppErrorIsAppId => AppError == InstagramAppSettingsService.AppIdError;

    public bool AppErrorIsSecret =>
        AppError is InstagramAppSettingsService.SecretLengthError or InstagramAppSettingsService.SecretMissingError;

    public bool ConfirmNeeded => AppError == InstagramAppSettingsService.ConfirmError;

    public string RedirectUri => InstagramPaths.RedirectUri(site.Value.BaseUrl);

    public string? ExpiresAtText => Status.ExpiresAt is { } value ? AthensTime.Format(value) : null;

    public string? ConnectedAtText => Status.ConnectedAt is { } value ? AthensTime.Format(value) : null;

    public string? LastFetchText => Describe(Activity.LastFetchAt, Activity.LastFetchResult, FetchTexts);

    public string? LastRefreshText => Describe(Activity.LastRefreshAt, Activity.LastRefreshResult, RefreshTexts);

    public async Task OnGetAsync(string? result, CancellationToken ct)
    {
        await LoadAsync(ct);
        AppId = AppStatus.AppId;
        if (result == "notconfigured")
        {
            (BannerIsNotSetUp, BannerIsError) = (true, true);
        }
        else if (result is not null && Banners.TryGetValue(result, out var banner))
        {
            (BannerText, BannerIsError) = banner;
        }
    }

    public async Task<IActionResult> OnPostConnectAsync(CancellationToken ct)
    {
        var credentials = await appSettings.GetCredentialsAsync(ct);
        if (credentials is null)
        {
            return LocalRedirect(InstagramPaths.ResultUrl("notconfigured"));
        }

        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminUserId))
        {
            return Forbid();
        }

        var state = oauthState.Create(HttpContext, adminUserId);
        return Redirect(client.BuildAuthorizeUrl(credentials, RedirectUri, state));
    }

    public async Task<IActionResult> OnPostSaveAppAsync(CancellationToken ct)
    {
        var result = await appSettings.SaveAsync(AppId, NewSecret, ConfirmDisconnect, User.Identity?.Name, ClientIp, ct);
        if (!result.Succeeded)
        {
            AppError = result.Error;
            await LoadAsync(ct);
            return Page();
        }

        return LocalRedirect(InstagramPaths.ResultUrl(result.Disconnected ? "app-saved-disconnected" : "app-saved"));
    }

    public async Task<IActionResult> OnPostRemoveAppAsync(CancellationToken ct)
    {
        await appSettings.RemoveAsync(User.Identity?.Name, ClientIp, ct);
        return LocalRedirect(InstagramPaths.ResultUrl("app-removed"));
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

    private async Task LoadAsync(CancellationToken ct)
    {
        Status = await connection.GetStatusAsync(ct);
        AppStatus = await appSettings.GetStatusAsync(ct);
        Activity = await feed.GetActivityAsync(ct);
        SchedulesOn = await schedule.IsSectionActiveAsync(ct);
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
