using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.EloRegistry;
using LgymApi.Application.Features.EloRegistry.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;

public interface IEloRegistryApiCompatibilityService
{
    Task<Result<List<EloRegistryChartEntry>, AppError>> GetChartAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
}

internal sealed class EloRegistryApiCompatibilityService : IEloRegistryApiCompatibilityService
{
    private readonly IEloRegistryService _eloRegistryService;

    public EloRegistryApiCompatibilityService(IEloRegistryService eloRegistryService)
    {
        _eloRegistryService = eloRegistryService;
    }

    public Task<Result<List<EloRegistryChartEntry>, AppError>> GetChartAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => _eloRegistryService.GetChartAsync(accountId, cancellationToken);
}
