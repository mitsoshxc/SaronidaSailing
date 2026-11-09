using System.Globalization;
using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Logging;

public sealed class AuditLog(ILoggerFactory loggerFactory) : IAuditLog
{
    public const string LoggerName = "Audit";
    public const int MaxFieldLength = 256;

    private readonly ILogger _logger = loggerFactory.CreateLogger(LoggerName);

    public void Record(string eventName, string? userName, string? ipAddress, string? details = null)
    {
        var name = Sanitize(eventName);
        var user = Sanitize(userName);
        var ip = Sanitize(ipAddress);
        var detail = Sanitize(details);

        user = user.Length == 0 ? "-" : user;
        ip = ip.Length == 0 ? "-" : ip;

        if (detail.Length == 0)
        {
            _logger.LogInformation("{AuditEvent}|user={AuditUser}|ip={AuditIp}", name, user, ip);
        }
        else
        {
            _logger.LogInformation("{AuditEvent}|user={AuditUser}|ip={AuditIp}|{AuditDetails}", name, user, ip, detail);
        }
    }

    // Fields can carry attacker input (e.g. the email typed on the login form), so they must not break the line format.
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Length > MaxFieldLength ? value[..MaxFieldLength] : value;
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c == '|' ? '/' : char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? ' ' : c;
            }
        });
    }
}
