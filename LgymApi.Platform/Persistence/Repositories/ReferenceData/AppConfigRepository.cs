using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.ReferenceData;

internal sealed class AppConfigRepository : IAppConfigRepository
{
    private readonly IPlatformPersistenceContext _persistenceContext;
    private readonly IGridifyExecutionService _gridifyExecutionService;
    private readonly IMapperRegistry _mapperRegistry;

    private static readonly PaginationPolicy AppConfigPaginationPolicy = new()
    {
        MaxPageSize = 100,
        DefaultPageSize = 20,
        DefaultSortField = "createdAt",
        TieBreakerField = "id"
    };

    public AppConfigRepository(IPlatformPersistenceContext persistenceContext, IGridifyExecutionService gridifyExecutionService, IMapperRegistry mapperRegistry)
    {
        _persistenceContext = persistenceContext;
        _gridifyExecutionService = gridifyExecutionService;
        _mapperRegistry = mapperRegistry;
    }

    public Task<AppConfig?> GetLatestByPlatformAsync(Platforms platform, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.AppConfigs
            .AsNoTracking()
            .Where(c => c.Platform == platform)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task AddAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.AppConfigs.AddAsync(config, cancellationToken).AsTask();
    }

    public Task<AppConfig?> FindByIdAsync(Id<AppConfig> id, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.AppConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public Task<AppConfig?> FindByIdTrackedAsync(Id<AppConfig> id, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.AppConfigs
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Pagination<AppConfig>> GetPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default)
    {
        var baseQuery = _persistenceContext.AppConfigs
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .AsQueryable();

        return await _gridifyExecutionService.ExecuteAsync(
            baseQuery,
            filterInput,
            _mapperRegistry,
            AppConfigPaginationPolicy,
            cancellationToken);
    }

    public void Update(AppConfig config)
    {
        _persistenceContext.AppConfigs.Update(config);
    }

    public void Delete(AppConfig config)
    {
        _persistenceContext.AppConfigs.Remove(config);
    }
}
