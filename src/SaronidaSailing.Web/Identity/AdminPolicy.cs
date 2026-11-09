namespace SaronidaSailing.Web.Identity;

public static class AdminPolicy
{
    public const string Name = "Admin";
    public const string MfaClaimType = "amr";
    public const string MfaClaimValue = "mfa";
}
