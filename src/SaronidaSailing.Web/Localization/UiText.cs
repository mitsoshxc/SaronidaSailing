using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Web.Localization;

public static class UiText
{
    public static string OpensInNewTab(string locale) =>
        locale == ContentLocales.English ? " (opens in a new tab)" : " (ανοίγει σε νέα καρτέλα)";
}
