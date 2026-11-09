using Microsoft.Extensions.DependencyInjection;
using NLog;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Cli;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class AdminCommandRunnerTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private sealed class FakePrompt(params string[] answers) : IPasswordPrompt
    {
        private readonly Queue<string> _answers = new(answers);

        public string Read(string label) => _answers.Dequeue();
    }

    private sealed class FakeReader(string? line) : ILineReader
    {
        public string? ReadLine() => line;
    }

    private sealed record Run(int ExitCode, string Output, string Error);

    private async Task<Run> RunAsync(string[] args, IPasswordPrompt? prompt = null, string? answer = null)
    {
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new AdminCommandRunner(
            scope.ServiceProvider.GetRequiredService<IAdminAccountService>(),
            scope.ServiceProvider.GetRequiredService<IAuditLog>(),
            prompt ?? new FakePrompt(),
            new FakeReader(answer),
            output,
            error);
        var code = await runner.RunAsync(args);
        return new Run(code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Add_Succeeds_AndAuditNeverContainsPassword()
    {
        var run = await RunAsync(["admin", "add", "cli-add@example.com"], new FakePrompt("secret-pass-AAA111", "secret-pass-AAA111"));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Admin cli-add@example.com added.", run.Output);
        Assert.Contains("two-factor", run.Output);

        LogManager.Flush();
        var audit = ReadAudit();
        Assert.Contains("admin-added|user=cli|ip=-|cli-add@example.com", audit);
        Assert.DoesNotContain("secret-pass-AAA111", audit);
    }

    [Fact]
    public async Task Add_PasswordMismatch_CreatesNothing()
    {
        var run = await RunAsync(["admin", "add", "cli-mismatch@example.com"], new FakePrompt("secret-pass-BBB222", "different-pass-CCC"));
        var list = await RunAsync(["admin", "list"]);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("do not match", run.Error);
        Assert.DoesNotContain("cli-mismatch@example.com", list.Output);
    }

    [Fact]
    public async Task Add_IdentityErrors_AreAuditedWithoutPassword()
    {
        var run = await RunAsync(["admin", "add", "cli-short@example.com"], new FakePrompt("tiny-DDD", "tiny-DDD"));

        Assert.Equal(1, run.ExitCode);
        Assert.NotEmpty(run.Error);
        LogManager.Flush();
        var audit = ReadAudit();
        Assert.Contains("admin-add-failed", audit);
        Assert.DoesNotContain("tiny-DDD", audit);
    }

    [Fact]
    public async Task ResetPassword_UnknownEmail_FailsAndAudits()
    {
        var run = await RunAsync(["admin", "reset-password", "nobody@example.com"], new FakePrompt("secret-pass-EEE333", "secret-pass-EEE333"));

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No admin with that email.", run.Error);
        LogManager.Flush();
        var audit = ReadAudit();
        Assert.Contains("admin-password-reset-failed", audit);
        Assert.DoesNotContain("secret-pass-EEE333", audit);
    }

    [Fact]
    public async Task ResetPassword_Succeeds()
    {
        await RunAsync(["admin", "add", "cli-reset@example.com"], new FakePrompt("secret-pass-FFF444", "secret-pass-FFF444"));

        var run = await RunAsync(["admin", "reset-password", "cli-reset@example.com"], new FakePrompt("secret-pass-GGG555", "secret-pass-GGG555"));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Password reset for cli-reset@example.com. Lockout cleared.", run.Output);
        LogManager.Flush();
        Assert.DoesNotContain("secret-pass-GGG555", ReadAudit());
    }

    [Fact]
    public async Task Remove_NeedsYes()
    {
        await RunAsync(["admin", "add", "cli-remove@example.com"], new FakePrompt("secret-pass-HHH666", "secret-pass-HHH666"));

        var cancelled = await RunAsync(["admin", "remove", "cli-remove@example.com"], answer: "n");
        var stillThere = await RunAsync(["admin", "list"]);
        var removed = await RunAsync(["admin", "remove", "cli-remove@example.com"], answer: "YES");
        var gone = await RunAsync(["admin", "list"]);

        Assert.Equal(1, cancelled.ExitCode);
        Assert.Contains("Cancelled.", cancelled.Output);
        Assert.Contains("cli-remove@example.com", stillThere.Output);
        Assert.Equal(0, removed.ExitCode);
        Assert.Contains("Admin cli-remove@example.com removed.", removed.Output);
        Assert.DoesNotContain("cli-remove@example.com", gone.Output);
    }

    [Fact]
    public async Task List_ShowsTwoFactorState()
    {
        await RunAsync(["admin", "add", "cli-list@example.com"], new FakePrompt("secret-pass-III777", "secret-pass-III777"));

        var run = await RunAsync(["admin", "list"]);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("cli-list@example.com  2FA off", run.Output);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("admin", "bogus")]
    [InlineData("admin", "add")]
    [InlineData("admin", "remove")]
    [InlineData("admin", "reset-password")]
    public async Task UsageErrors_Return2(params string[] args)
    {
        var run = await RunAsync(args);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Usage:", run.Error);
    }

    private string ReadAudit()
    {
        var path = Path.Combine(factory.LogsPath, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.log");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
