namespace LgymApi.Application.Pagination;

public sealed record FilterInput
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public List<FilterGroup> FilterGroups { get; init; } = [];

    public List<SortDescriptor> SortDescriptors { get; init; } = [];
}

public sealed record FilterGroup
{
    public required GroupOperator Operator { get; init; }

    public List<FilterCondition> Conditions { get; init; } = [];

    public List<FilterGroup> Groups { get; init; } = [];
}

public enum GroupOperator
{
    And = 0,
    Or = 1
}
