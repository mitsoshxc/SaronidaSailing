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
    };

    public InstagramStatus Status { get; private set; } = null!;

    public string? BannerText { get; private set; }

    public bool BannerIsError { get; private set; }

    public string? ExpiresAtText => Status.ExpiresAt is { } value ? AthensTime.Format(value) : null;

    public string? ConnectedAtText => Status.ConnectedAt is { } value ? AthensTime.Format(value) : null;

    public async Task OnGetAsync(string? result, CancellationToken ct)
    {
        Status = await connection.GetStatusAsync(ct);
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

    public async Task<IActionResult> OnPostDisconnectAsync(CancellationToken ct)
    {
        await connection.DisconnectAsync(User.Identity?.Name ?? "", ClientIp, ct);
        return LocalRedirect(InstagramPaths.Admin);
    }
}
