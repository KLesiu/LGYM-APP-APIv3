namespace LgymApi.Application.Pagination;

public abstract class PaginatedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public List<FilterGroup> FilterGroups { get; init; } = [];
    public List<SortDescriptor> SortDescriptors { get; init; } = [];
}
