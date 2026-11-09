namespace SaronidaSailing.Web.Cli;

public interface IPasswordPrompt
{
    string Read(string label);
}

public interface ILineReader
{
    string? ReadLine();
}
