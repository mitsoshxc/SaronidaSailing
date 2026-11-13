using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Instagram;

public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public const string Purpose = "SaronidaSailing.Instagram.AccessToken.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public bool TryUnprotect(string protectedValue, out string plaintext)
    {
        try
        {
            plaintext = _protector.Unprotect(protectedValue);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            plaintext = "";
            return false;
        }
    }
}
