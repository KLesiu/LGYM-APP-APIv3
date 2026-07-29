namespace LgymApi.Application.Pagination;

public sealed class Pagination<TProjection>
{
    public List<TProjection> Items { get; init; } = [];

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public bool HasNextPage => PageSize > 0 && Page * PageSize < TotalCount;

    public bool HasPreviousPage => Page > 1;
}
