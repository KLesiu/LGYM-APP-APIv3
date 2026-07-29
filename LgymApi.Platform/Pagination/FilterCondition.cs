namespace LgymApi.Application.Pagination;

public sealed record FilterCondition
{
    public required string FieldName { get; init; }

    public required FilterOperator Operator { get; init; }

    public object? Value { get; init; }
}

public enum FilterOperator
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    StartsWith = 3,
    EndsWith = 4,
    In = 5,
    GreaterThan = 6,
    GreaterThanOrEqual = 7,
    LessThan = 8,
    LessThanOrEqual = 9
}
