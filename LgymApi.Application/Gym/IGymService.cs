using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Gym.Models;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;
using GymEntity = LgymApi.Domain.Entities.Gym;

namespace LgymApi.Application.Features.Gym;

public interface IGymService
{
    Task<Result<Unit, AppError>> AddGymAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.User> routeUserId, string name, string? address, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteGymAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, CancellationToken cancellationToken = default);
    Task<Result<GymListContext, AppError>> GetGymsAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.User> routeUserId, CancellationToken cancellationToken = default);
    Task<Result<GymEntity, AppError>> GetGymAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateGymAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, string name, string? address, CancellationToken cancellationToken = default);
}
