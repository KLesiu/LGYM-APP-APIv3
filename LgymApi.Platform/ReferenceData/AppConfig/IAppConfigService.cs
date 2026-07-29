using AppConfigEntity = LgymApi.Domain.Entities.AppConfig;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Platform.ReferenceData.AppConfig;

// CreateAppVersionInput and UpdateAppConfigInput are in the same namespace

public interface IAppConfigService
{
    Task<Result<AppConfigEntity, AppError>> GetLatestByPlatformAsync(Platforms platform, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> CreateNewAppVersionAsync(Id<LgymApi.Domain.Entities.User> userId, CreateAppVersionInput input, CancellationToken cancellationToken = default);
    Task<Result<Pagination<AppConfigEntity>, AppError>> GetPaginatedAsync(Id<LgymApi.Domain.Entities.User> userId, FilterInput filterInput, CancellationToken cancellationToken = default);
    Task<Result<AppConfigEntity, AppError>> GetByIdAsync(Id<LgymApi.Domain.Entities.User> userId, Id<AppConfigEntity> configId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateAsync(Id<LgymApi.Domain.Entities.User> userId, Id<AppConfigEntity> configId, UpdateAppConfigInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(Id<LgymApi.Domain.Entities.User> userId, Id<AppConfigEntity> configId, CancellationToken cancellationToken = default);
}
