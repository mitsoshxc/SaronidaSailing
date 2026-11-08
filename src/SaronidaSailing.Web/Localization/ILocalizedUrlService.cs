namespace SaronidaSailing.Web.Localization;

public interface ILocalizedUrlService
{
    string GetPath(string pageKey, string culture);

    SitePage? GetCurrentPage();

    string? GetAlternatePath();
}
