namespace SaronidaSailing.Core.Interfaces;

public record AdminResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static AdminResult Success { get; } = new(true, []);

    public static AdminResult Failure(params string[] errors) => new(false, errors);
}

public record AdminSummary(string Email, bool TwoFactorEnabled, DateTime? LockedOutUntilUtc);

public interface IAdminAccountService
{
    Task<AdminResult> AddAsync(string email, string password);

    Task<AdminResult> ResetPasswordAsync(string email, string password);

    Task<IReadOnlyList<AdminSummary>> ListAsync();

    Task<AdminResult> RemoveAsync(string email);
}
