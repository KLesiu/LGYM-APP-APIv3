using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Pagination;

public interface IQueryPaginationService
{
    Task<Result<Pagination<TProjection>, AppError>> ExecuteAsync<TProjection>(
        Func<IQueryable<TProjection>> queryFactory,
        FilterInput filterInput,
        CancellationToken cancellationToken = default)
        where TProjection : class;
}
