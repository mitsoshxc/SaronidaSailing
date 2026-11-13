namespace SaronidaSailing.Infrastructure.Instagram;

public sealed class InstagramOptions
{
    public const string SectionName = "Instagram";

    public string AppId { get; set; } = "";

    public string AppSecret { get; set; } = "";

    public string AuthorizeUrl { get; set; } = "https://www.instagram.com/oauth/authorize";

    public string ApiBaseUrl { get; set; } = "https://api.instagram.com";

    public string GraphBaseUrl { get; set; } = "https://graph.instagram.com";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret);
}
