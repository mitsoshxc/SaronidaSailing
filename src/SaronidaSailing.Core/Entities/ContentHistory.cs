namespace SaronidaSailing.Core.Entities;

public class ContentHistory : BaseEntity
{
    public required Guid ContentItemId { get; init; }

    public required string Value { get; init; }

    public required string ChangedBy { get; init; }
}
