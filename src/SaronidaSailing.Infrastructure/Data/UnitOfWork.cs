using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Data;

public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct = default) => await db.SaveChangesAsync(ct);
}
