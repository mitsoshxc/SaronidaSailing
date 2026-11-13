using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace SaronidaSailing.Web.Instagram;

public enum InstagramOAuthStatus
{
    Valid,
    Expired,
    Invalid
}

public sealed record InstagramOAuthResult(InstagramOAuthStatus Status, string? AdminUserId)
{
    public static readonly InstagramOAuthResult Invalid = new(InstagramOAuthStatus.Invalid, null);
    public static readonly InstagramOAuthResult Expired = new(InstagramOAuthStatus.Expired, null);
}

public sealed class InstagramOAuthState(IDataProtectionProvider provider, TimeProvider time)
{
    public const string CookieName = "saronida.ig-oauth";
    public const string Purpose = "SaronidaSailing.Instagram.OAuthState.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    private sealed record Payload(string AdminUserId, string Nonce, long ExpiresAtUnix);

    public string Create(HttpContext http, string adminUserId)
    {
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expires = time.GetUtcNow().Add(Lifetime);
        var state = _protector.Protect(JsonSerializer.Serialize(new Payload(adminUserId, nonce, expires.ToUnixTimeSeconds())));

        // Lax, not Strict: the admin auth cookie is SameSite=Strict and is not sent on the cross-site return
        // from instagram.com, but Lax cookies are sent on a top-level GET navigation.
        http.Response.Cookies.Append(CookieName, nonce, CookieOptions(Lifetime));
        return state;
    }

    public InstagramOAuthResult Validate(HttpContext http, string? state)
    {
        var cookie = http.Request.Cookies[CookieName];
        http.Response.Cookies.Delete(CookieName, CookieOptions(null));

        Payload? payload;
        try
        {
            payload = string.IsNullOrEmpty(state)
                ? null
                : JsonSerializer.Deserialize<Payload>(_protector.Unprotect(state));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return InstagramOAuthResult.Invalid;
        }

        if (payload is null || string.IsNullOrEmpty(payload.AdminUserId) || string.IsNullOrEmpty(payload.Nonce))
        {
            return InstagramOAuthResult.Invalid;
        }

        if (time.GetUtcNow().ToUnixTimeSeconds() >= payload.ExpiresAtUnix)
        {
            return InstagramOAuthResult.Expired;
        }

        if (string.IsNullOrEmpty(cookie)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(payload.Nonce)))
        {
            return InstagramOAuthResult.Invalid;
        }

        return new InstagramOAuthResult(InstagramOAuthStatus.Valid, payload.AdminUserId);
    }

    private static CookieOptions CookieOptions(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = InstagramPaths.Admin,
        MaxAge = maxAge,
        IsEssential = true,
    };
}
