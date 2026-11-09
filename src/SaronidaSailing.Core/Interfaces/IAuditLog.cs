namespace SaronidaSailing.Core.Interfaces;

public interface IAuditLog
{
    void Record(string eventName, string? userName, string? ipAddress, string? details = null);
}
