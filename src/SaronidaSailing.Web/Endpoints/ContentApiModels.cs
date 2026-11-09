using System.Text.Json.Serialization;
using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Web.Endpoints;

public sealed record SaveContentRequest
{
    public required IReadOnlyList<ContentChangeRequest?> Changes { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(SetFieldRequest), "setField")]
[JsonDerivedType(typeof(AddListItemRequest), "addListItem")]
[JsonDerivedType(typeof(DeleteListItemRequest), "deleteListItem")]
[JsonDerivedType(typeof(ReorderListRequest), "reorderList")]
[JsonDerivedType(typeof(SetListItemActiveRequest), "setListItemActive")]
[JsonDerivedType(typeof(SetSectionActiveRequest), "setSectionActive")]
public abstract record ContentChangeRequest;

public sealed record SetFieldRequest : ContentChangeRequest
{
    public required string Key { get; init; }

    public required string Locale { get; init; }

    public required ContentType Type { get; init; }

    public required string Value { get; init; }

    public Guid? ListItemId { get; init; }
}

public sealed record AddListItemRequest : ContentChangeRequest
{
    public required string ListKey { get; init; }

    public required Guid Id { get; init; }
}

public sealed record DeleteListItemRequest : ContentChangeRequest
{
    public required string ListKey { get; init; }

    public required Guid Id { get; init; }
}

public sealed record ReorderListRequest : ContentChangeRequest
{
    public required string ListKey { get; init; }

    public required IReadOnlyList<Guid> OrderedIds { get; init; }
}

public sealed record SetListItemActiveRequest : ContentChangeRequest
{
    public required string ListKey { get; init; }

    public required Guid Id { get; init; }

    public required bool IsActive { get; init; }
}

public sealed record SetSectionActiveRequest : ContentChangeRequest
{
    public required string SectionKey { get; init; }

    public required bool IsActive { get; init; }
}

public sealed record ErrorsResponse(IReadOnlyList<string> Errors);

public sealed record SaveContentResponse(Guid? SaveId);

public sealed record FieldVersionResponse(Guid SaveId, string Value, string ChangedBy, DateTime CreatedAt);

public sealed record SavedFieldResponse(
    string Key, string Locale, ContentType Type, Guid? ListItemId, string Value, string PreviousValue, string CurrentValue);

public sealed record ContentSaveResponse(
    Guid SaveId, string ChangedBy, DateTime CreatedAt, IReadOnlyList<SavedFieldResponse> Fields);
