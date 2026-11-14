using Microsoft.AspNetCore.Diagnostics;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Pages.Errors;

public class ServerErrorModel(ILocalizedUrlService urlService) : ErrorPageModel(urlService)
{
    protected override int Status => StatusCodes.Status500InternalServerError;

    protected override PathString? OriginalPath =>
        HttpContext.Features.Get<IExceptionHandlerPathFeature>() is { } feature
            ? new PathString(feature.Path)
            : null;
}
