using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Pagination;

namespace LgymApi.Infrastructure.Pagination;

public sealed class QueryPaginationService(
    IGridifyExecutionService gridifyExecutionService,
    IMapperRegistry mapperRegistry,
    PaginationPolicy paginationPolicy) : IQueryPaginationService
{
    public async Task<Result<Pagination<TProjection>, AppError>> ExecuteAsync<TProjection>(
        Func<IQueryable<TProjection>> queryFactory,
        FilterInput filterInput,
        CancellationToken cancellationToken = default)
        where TProjection : class
    {
        ArgumentNullException.ThrowIfNull(queryFactory);
        ArgumentNullException.ThrowIfNull(filterInput);

        try
        {
            var baseQuery = queryFactory();
            ArgumentNullException.ThrowIfNull(baseQuery);

            var pagination = await gridifyExecutionService.ExecuteAsync(
                baseQuery,
                filterInput,
                mapperRegistry,
                paginationPolicy,
                cancellationToken);

            return Result.Success<Pagination<TProjection>, AppError>(pagination);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result.Failure<Pagination<TProjection>, AppError>(new BadRequestError(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<Pagination<TProjection>, AppError>(new BadRequestError(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<Pagination<TProjection>, AppError>(new BadRequestError(ex.Message));
        }
        catch (Exception)
        {
            return Result.Failure<Pagination<TProjection>, AppError>(
                new InternalServerError("Pagination failed."));
        }
    }
}
