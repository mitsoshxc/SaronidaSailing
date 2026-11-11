using System.Linq;
using Microsoft.Extensions.Configuration;

namespace SaronidaSailing.Web;

public static class LogInstance
{
    public static string Resolve(IConfiguration configuration)
    {
        var configured = configuration["Logs:Instance"];
        var instance = (string.IsNullOrWhiteSpace(configured) ? Environment.MachineName : configured).Trim();

        if (instance.Length == 0 || instance.Contains('|') || instance.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Logs:Instance must be non-empty and must not contain '|' or control characters (it is written into every log line).");
        }

        return instance;
    }
}
