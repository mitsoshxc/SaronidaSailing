namespace SaronidaSailing.Core.Interfaces;

public interface ISecretProtector
{
    string Protect(string plaintext);

    bool TryUnprotect(string protectedValue, out string plaintext);
}
