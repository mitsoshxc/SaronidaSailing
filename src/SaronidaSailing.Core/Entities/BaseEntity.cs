namespace SaronidaSailing.Core.Entities;

public abstract class BaseEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }
}
