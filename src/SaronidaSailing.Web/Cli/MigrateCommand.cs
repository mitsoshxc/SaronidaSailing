using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Web.Cli;

public sealed class MigrateCommand(IServiceProvider services, TextWriter output, TextWriter error)
{
    public const string Command = "migrate";

    public static bool IsMigrateCommand(string[] args) => args.Length > 0 && args[0] == Command;

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 1)
        {
            await error.WriteLineAsync("Usage: migrate");
            return 2;
        }

        try
        {
            var result = await services.MigrateAsync();
            var count = result.AppliedMigrations.Count;
            var applied = count == 0
                ? "No pending migrations."
                : $"Applied {count} migration{(count == 1 ? "" : "s")}: {string.Join(", ", result.AppliedMigrations)}.";
            await output.WriteLineAsync($"{applied} Hangfire schema up to date.");
            return 0;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"Migration failed: {ex}");
            return 1;
        }
    }
}
