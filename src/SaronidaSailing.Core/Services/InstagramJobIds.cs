namespace SaronidaSailing.Core.Services;

public static class InstagramJobIds
{
    public const string Fetch = "instagram-fetch";
    public const string TokenRefresh = "instagram-token-refresh";

    public const string FetchCron = "0 6,18 * * *";
    public const string TokenRefreshCron = "0 4 * * 1";
}
