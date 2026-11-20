using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Infrastructure.Identity;

public sealed class ProtectedTokenUserStore(
    AppDbContext context,
    IDataProtectionProvider dataProtection,
    ILogger<ProtectedTokenUserStore> logger,
    IdentityErrorDescriber? describer = null)
    : UserOnlyStore<ApplicationUser, AppDbContext, string, IdentityUserClaim<string>, IdentityUserLogin<string>, IdentityUserToken<string>>(context, describer)
{
    public const string Purpose = "SaronidaSailing.Identity.Tokens.v1";

    private const string InternalLoginProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodeTokenName = "RecoveryCodes";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);

    public override Task SetTokenAsync(ApplicationUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        var stored = value is not null && IsProtected(loginProvider, name) ? _protector.Protect(value) : value;
        return base.SetTokenAsync(user, loginProvider, name, stored, cancellationToken);
    }

    public override async Task<string?> GetTokenAsync(ApplicationUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        var stored = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        if (stored is null || !IsProtected(loginProvider, name))
        {
            return stored;
        }

        try
        {
            return _protector.Unprotect(stored);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            logger.LogWarning("Identity token {TokenName} of user {UserId} cannot be unprotected and is treated as not set", name, user.Id);
            return null;
        }
    }

    private static bool IsProtected(string loginProvider, string name) =>
        loginProvider == InternalLoginProvider && name is AuthenticatorKeyTokenName or RecoveryCodeTokenName;
}
