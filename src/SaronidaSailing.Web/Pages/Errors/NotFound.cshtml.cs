using Microsoft.AspNetCore.Diagnostics;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Pages.Errors;

public class NotFoundModel(ILocalizedUrlService urlService) : ErrorPageModel(urlService)
{
    protected override int Status =>
        int.TryParse(RouteData.Values["code"]?.ToString(), out var code) && code == StatusCodes.Status410Gone
            ? code
            : StatusCodes.Status404NotFound;

    protected override PathString? OriginalPath =>
        HttpContext.Features.Get<IStatusCodeReExecuteFeature>() is { } feature
            ? new PathString(feature.OriginalPath)
            : null;
}
