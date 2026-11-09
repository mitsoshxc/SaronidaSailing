using System.Text;

namespace SaronidaSailing.Web.Cli;

public sealed class ConsolePasswordPrompt : IPasswordPrompt
{
    public string Read(string label)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("This command needs an interactive terminal (use docker compose exec with a TTY).");
        }

        Console.Error.Write(label);
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return password.ToString();
    }
}

public sealed class ConsoleLineReader : ILineReader
{
    public string? ReadLine() => Console.In.ReadLine();
}
