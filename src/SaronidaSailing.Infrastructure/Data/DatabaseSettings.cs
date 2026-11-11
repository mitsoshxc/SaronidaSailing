namespace SaronidaSailing.Infrastructure.Data;

public sealed record DatabaseSettings(bool MigrateAtStartup)
{
    public const string MigrateAtStartupKey = "Database:MigrateAtStartup";
}
