using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Pagination;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using AppConfigEntity = LgymApi.Domain.Entities.AppConfig;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Task7ApiCompatibility;

public interface IAppConfigApiCompatibilityAdapter
{
    Task<Result<Unit, AppError>> CreateNewAppVersionAsync(
        Id<AccountReference> accountId,
        CreateAppVersionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<Pagination<AppConfigEntity>, AppError>> GetPaginatedAsync(
        Id<AccountReference> accountId,
        FilterInput filterInput,
        CancellationToken cancellationToken = default);

    Task<Result<AppConfigEntity, AppError>> GetByIdAsync(
        Id<AccountReference> accountId,
        Id<AppConfigEntity> configId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> UpdateAsync(
        Id<AccountReference> accountId,
        Id<AppConfigEntity> configId,
        UpdateAppConfigInput input,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> DeleteAsync(
        Id<AccountReference> accountId,
        Id<AppConfigEntity> configId,
        CancellationToken cancellationToken = default);
}

internal sealed class AppConfigApiCompatibilityAdapter : IAppConfigApiCompatibilityAdapter
{
    private readonly IAppConfigService _appConfigService;

    public AppConfigApiCompatibilityAdapter(IAppConfigService appConfigService)
    {
        _appConfigService = appConfigService;
    }

    public Task<Result<Unit, AppError>> CreateNewAppVersionAsync(
        Id<AccountReference> accountId,
        CreateAppVersionInput input,
        CancellationToken cancellationToken = default)
        => _appConfigService.CreateNewAppVersionAsync(accountId.Rebind<UserEntity>(), input, cancellationToken);

    public Task<Result<Pagination<AppConfigEntity>, AppError>> GetPaginatedAsync(
        Id<AccountReference> accountId,
        FilterInput filterInput,
        CancellationToken cancellationToken = default)
        => _appConfigService.GetPaginatedAsync(accountId.Rebind<UserEntity>(), filterInput, cancellationToken);

    public Task<Result<AppConfigEntity, AppError>> GetByIdAsync(
        Id<AccountReference> accountId,
        Id<AppConfigEntity> configId,
        CancellationToken cancellationToken = default)
        => _appConfigService.GetByIdAsync(accountId.Rebind<UserEntity>(), configId, cancellationToken);

    public Task<Result<Unit, AppError>> UpdateAsync(
        Id<AccountReference> accountId,
        Id<AppConfigEntity> configId,
        UpdateAppConfigInput input,
        CancellationToken cancellationToken = default)
        => _appConfigService.UpdateAsync(accountId.Rebind<UserEntity>(), configId, input, cancellationToken);

    public Task<Result<Unit, AppError>> DeleteAsync(
        Id<AccountReference> accountId,
        Id<AppConfigEntity> configId,
        CancellationToken cancellationToken = default)
        => _appConfigService.DeleteAsync(accountId.Rebind<UserEntity>(), configId, cancellationToken);
}
