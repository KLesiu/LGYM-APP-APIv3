using LgymApi.Application.Pagination;
using AppConfigEntity = LgymApi.Domain.Entities.AppConfig;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

public interface IAppConfigRepository
{
    Task<AppConfigEntity?> GetLatestByPlatformAsync(Platforms platform, CancellationToken cancellationToken = default);
    Task AddAsync(AppConfigEntity config, CancellationToken cancellationToken = default);
    Task<AppConfigEntity?> FindByIdAsync(Id<AppConfigEntity> id, CancellationToken cancellationToken = default);
    Task<AppConfigEntity?> FindByIdTrackedAsync(Id<AppConfigEntity> id, CancellationToken cancellationToken = default);
    Task<Pagination<AppConfigEntity>> GetPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default);
    void Update(AppConfigEntity config);
    void Delete(AppConfigEntity config);
}
