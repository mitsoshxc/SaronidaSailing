using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Logging;

public sealed class AuditLog(ILoggerFactory loggerFactory) : IAuditLog
{
    public const string LoggerName = "Audit";

    private readonly ILogger _logger = loggerFactory.CreateLogger(LoggerName);

    public void Record(string eventName, string? userName, string? ipAddress, string? details = null)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? "-" : userName;
        var ip = string.IsNullOrWhiteSpace(ipAddress) ? "-" : ipAddress;

        if (string.IsNullOrWhiteSpace(details))
        {
            _logger.LogInformation("{AuditEvent}|user={AuditUser}|ip={AuditIp}", eventName, user, ip);
        }
        else
        {
            _logger.LogInformation("{AuditEvent}|user={AuditUser}|ip={AuditIp}|{AuditDetails}", eventName, user, ip, details);
        }
    }
}
