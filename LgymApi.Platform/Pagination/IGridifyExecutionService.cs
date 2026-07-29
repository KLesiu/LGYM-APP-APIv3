using LgymApi.Application.Pagination;

namespace LgymApi.Infrastructure.Pagination;

public interface IGridifyExecutionService
{
    Task<Pagination<TProjection>> ExecuteAsync<TProjection>(
        IQueryable<TProjection> baseQuery,
        FilterInput filterInput,
        IMapperRegistry mapperRegistry,
        PaginationPolicy paginationPolicy,
        CancellationToken cancellationToken = default)
        where TProjection : class;
}
