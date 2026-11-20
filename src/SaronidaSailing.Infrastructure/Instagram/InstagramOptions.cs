namespace SaronidaSailing.Infrastructure.Instagram;

public sealed class InstagramOptions
{
    public const string SectionName = "Instagram";

    public static readonly string[] DefaultImageHosts = ["cdninstagram.com", "fbcdn.net"];

    public string AuthorizeUrl { get; set; } = "https://www.instagram.com/oauth/authorize";

    public string ApiBaseUrl { get; set; } = "https://api.instagram.com";

    public string GraphBaseUrl { get; set; } = "https://graph.instagram.com";

    public string[] ImageHosts { get; set; } = [];

    public static bool IsImageHost(string? host, string[] configuredHosts)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        var allowed = configuredHosts.Length == 0 ? DefaultImageHosts : configuredHosts;
        return allowed.Any(entry =>
            !string.IsNullOrWhiteSpace(entry)
            && (host.Equals(entry, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + entry, StringComparison.OrdinalIgnoreCase)));
    }
}
