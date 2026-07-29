namespace LgymApi.Application.Pagination;

public sealed class PaginationPolicy
{
    public int MaxPageSize { get; init; }

    public int DefaultPageSize { get; init; }

    public string DefaultSortField { get; init; } = string.Empty;

    public string TieBreakerField { get; init; } = string.Empty;
}
