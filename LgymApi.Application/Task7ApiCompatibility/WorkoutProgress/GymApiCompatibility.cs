using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Gym;
using LgymApi.Application.Features.Gym.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using GymEntity = LgymApi.Domain.Entities.Gym;

namespace LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;

public interface IGymApiCompatibilityService
{
    Task<Result<Unit, AppError>> AddGymAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, string name, string? address, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteGymAsync(AuthenticatedAccountContext? currentAccount, Id<GymEntity> gymId, CancellationToken cancellationToken = default);
    Task<Result<GymListContext, AppError>> GetGymsAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, CancellationToken cancellationToken = default);
    Task<Result<WorkoutGymPersistenceModel, AppError>> GetGymAsync(AuthenticatedAccountContext? currentAccount, Id<GymEntity> gymId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateGymAsync(AuthenticatedAccountContext? currentAccount, Id<GymEntity> gymId, string name, string? address, CancellationToken cancellationToken = default);
}

internal sealed class GymApiCompatibilityService : IGymApiCompatibilityService
{
    private readonly IGymService _gymService;

    public GymApiCompatibilityService(IGymService gymService)
    {
        _gymService = gymService;
    }

    public async Task<Result<Unit, AppError>> AddGymAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, string name, string? address, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _gymService.AddGymAsync(null, routeAccountId, name, address, cancellationToken);
        }

        return await _gymService.AddGymAsync(currentAccount, routeAccountId, name, address, cancellationToken);
    }

    public async Task<Result<Unit, AppError>> DeleteGymAsync(AuthenticatedAccountContext? currentAccount, Id<GymEntity> gymId, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _gymService.DeleteGymAsync(null, gymId, cancellationToken);
        }

        return await _gymService.DeleteGymAsync(currentAccount, gymId, cancellationToken);
    }

    public async Task<Result<GymListContext, AppError>> GetGymsAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _gymService.GetGymsAsync(null, routeAccountId, cancellationToken);
        }

        return await _gymService.GetGymsAsync(currentAccount, routeAccountId, cancellationToken);
    }

    public async Task<Result<WorkoutGymPersistenceModel, AppError>> GetGymAsync(AuthenticatedAccountContext? currentAccount, Id<GymEntity> gymId, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _gymService.GetGymAsync(null, gymId, cancellationToken);
        }

        return await _gymService.GetGymAsync(currentAccount, gymId, cancellationToken);
    }

    public async Task<Result<Unit, AppError>> UpdateGymAsync(AuthenticatedAccountContext? currentAccount, Id<GymEntity> gymId, string name, string? address, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _gymService.UpdateGymAsync(null, gymId, name, address, cancellationToken);
        }

        return await _gymService.UpdateGymAsync(currentAccount, gymId, name, address, cancellationToken);
    }

}
