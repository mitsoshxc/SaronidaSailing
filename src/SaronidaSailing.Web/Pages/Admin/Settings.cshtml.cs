using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Endpoints;
using SaronidaSailing.Web.Identity;

namespace SaronidaSailing.Web.Pages.Admin;

[Authorize(Policy = AdminPolicy.Name)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SettingsModel(
    ISiteSettingsService settings,
    IHostEnvironment environment,
    IOptions<SiteOptions> site) : AdminPageModel
{
    public const string Path = "/admin/settings";

    public const string RobotsTextFieldId = "RobotsText";

    [BindProperty]
    public string? RobotsText { get; set; }

    public string ServedText { get; private set; } = "";

    public string DefaultText => RobotsTxt.Default(site.Value.BaseUrl);

    public bool IsPreview => environment.IsPreview();

    public string? NoticeText { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(string? result, CancellationToken ct)
    {
        RobotsText = await settings.GetRobotsTxtAsync(ct);
        await LoadServedAsync(ct);
        NoticeText = result switch
        {
            "saved" => "robots.txt saved.",
            "default" => "robots.txt reset to the default.",
            _ => null,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await settings.SaveRobotsTxtAsync(RobotsText, User.Identity?.Name, ClientIp, ct);
        if (!result.Succeeded)
        {
            Error = result.Error;
            await LoadServedAsync(ct);
            return Page();
        }

        return LocalRedirect($"{Path}?result=saved");
    }

    public async Task<IActionResult> OnPostUseDefaultAsync(CancellationToken ct)
    {
        await settings.SaveRobotsTxtAsync(null, User.Identity?.Name, ClientIp, ct);
        return LocalRedirect($"{Path}?result=default");
    }

    private async Task LoadServedAsync(CancellationToken ct) =>
        ServedText = RobotsTxt.Served(IsPreview, IsPreview ? null : await settings.GetRobotsTxtAsync(ct), site.Value.BaseUrl);
}
