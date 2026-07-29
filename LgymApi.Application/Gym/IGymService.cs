using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Gym.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Features.Gym;

public interface IGymService
{
    Task<Result<Unit, AppError>> AddGymAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, string name, string? address, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteGymAsync(AuthenticatedAccountContext? currentAccount, Id<LgymApi.Domain.Entities.Gym> gymId, CancellationToken cancellationToken = default);
    Task<Result<GymListContext, AppError>> GetGymsAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, CancellationToken cancellationToken = default);
    Task<Result<WorkoutGymPersistenceModel, AppError>> GetGymAsync(AuthenticatedAccountContext? currentAccount, Id<LgymApi.Domain.Entities.Gym> gymId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateGymAsync(AuthenticatedAccountContext? currentAccount, Id<LgymApi.Domain.Entities.Gym> gymId, string name, string? address, CancellationToken cancellationToken = default);
}
