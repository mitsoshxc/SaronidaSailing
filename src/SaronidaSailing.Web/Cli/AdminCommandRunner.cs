using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Cli;

public sealed class AdminCommandRunner(
    IAdminAccountService admins,
    IAuditLog audit,
    IPasswordPrompt passwordPrompt,
    ILineReader lineReader,
    TextWriter output,
    TextWriter error)
{
    public const string Command = "admin";
    private const string AuditUser = "cli";

    public static bool IsAdminCommand(string[] args) => args.Length > 0 && args[0] == Command;

    public async Task<int> RunAsync(string[] args)
    {
        var name = args.Length > 1 ? args[1] : "";
        var email = args.Length > 2 ? args[2].Trim() : "";
        var hasEmail = email.Length > 0;

        switch (name)
        {
            case "add" when hasEmail && args.Length == 3:
                return await AddAsync(email);
            case "reset-password" when hasEmail && args.Length == 3:
                return await ResetPasswordAsync(email);
            case "remove" when hasEmail && args.Length == 3:
                return await RemoveAsync(email);
            case "list" when args.Length == 2:
                return await ListAsync();
            default:
                await error.WriteLineAsync("Usage: admin add <email> | admin reset-password <email> | admin list | admin remove <email>");
                return 2;
        }
    }

    private async Task<int> AddAsync(string email)
    {
        var password = PromptNewPassword();
        if (password is null)
        {
            return 1;
        }

        var result = await admins.AddAsync(email, password);
        if (!result.Succeeded)
        {
            audit.Record("admin-add-failed", AuditUser, null, $"{email}: {string.Join("; ", result.Errors)}");
            await PrintErrorsAsync(result);
            return 1;
        }

        audit.Record("admin-added", AuditUser, null, email);
        await output.WriteLineAsync($"Admin {email} added. They must set up two-factor authentication at first login.");
        return 0;
    }

    private async Task<int> ResetPasswordAsync(string email)
    {
        var password = PromptNewPassword();
        if (password is null)
        {
            return 1;
        }

        var result = await admins.ResetPasswordAsync(email, password);
        if (!result.Succeeded)
        {
            audit.Record("admin-password-reset-failed", AuditUser, null, $"{email}: {string.Join("; ", result.Errors)}");
            await PrintErrorsAsync(result);
            return 1;
        }

        audit.Record("admin-password-reset", AuditUser, null, email);
        await output.WriteLineAsync($"Password reset for {email}. Lockout cleared.");
        return 0;
    }

    private async Task<int> ListAsync()
    {
        var list = await admins.ListAsync();
        audit.Record("admin-listed", AuditUser, null, $"count={list.Count}");
        if (list.Count == 0)
        {
            await output.WriteLineAsync("No admins.");
            return 0;
        }

        foreach (var admin in list)
        {
            var line = $"{admin.Email}  2FA {(admin.TwoFactorEnabled ? "on" : "off")}";
            if (admin.LockedOutUntilUtc is { } until)
            {
                line += $"  locked until {until:yyyy-MM-ddTHH:mm:ssZ}";
            }

            await output.WriteLineAsync(line);
        }

        return 0;
    }

    private async Task<int> RemoveAsync(string email)
    {
        await output.WriteAsync($"Remove admin {email}? [y/N] ");
        var answer = lineReader.ReadLine()?.Trim();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            audit.Record("admin-remove-cancelled", AuditUser, null, email);
            await output.WriteLineAsync("Cancelled.");
            return 1;
        }

        var result = await admins.RemoveAsync(email);
        if (!result.Succeeded)
        {
            audit.Record("admin-remove-failed", AuditUser, null, $"{email}: {string.Join("; ", result.Errors)}");
            await PrintErrorsAsync(result);
            return 1;
        }

        audit.Record("admin-removed", AuditUser, null, email);
        await output.WriteLineAsync($"Admin {email} removed.");
        return 0;
    }

    private string? PromptNewPassword()
    {
        try
        {
            var password = passwordPrompt.Read("Password: ");
            var repeat = passwordPrompt.Read("Repeat password: ");
            if (password != repeat)
            {
                error.WriteLine("Passwords do not match.");
                return null;
            }

            return password;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine(ex.Message);
            return null;
        }
    }

    private async Task PrintErrorsAsync(AdminResult result)
    {
        foreach (var message in result.Errors)
        {
            await error.WriteLineAsync(message);
        }
    }
}
