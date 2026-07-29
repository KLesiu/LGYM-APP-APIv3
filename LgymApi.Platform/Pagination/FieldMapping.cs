namespace LgymApi.Application.Pagination;

public sealed record FieldMapping
{
    public required string FieldName { get; init; }

    public required string MemberName { get; init; }

    public bool AllowSort { get; init; } = true;

    public bool AllowFilter { get; init; } = true;
}

public sealed record SortDescriptor
{
    public required string FieldName { get; init; }

    public bool Descending { get; init; }
}
