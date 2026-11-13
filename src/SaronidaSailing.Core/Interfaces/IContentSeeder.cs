namespace SaronidaSailing.Core.Interfaces;

public interface IContentSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}
