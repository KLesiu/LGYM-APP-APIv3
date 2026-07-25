using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Gym.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Resources;
using LgymApi.Domain.ValueObjects;
using GymEntity = LgymApi.Domain.Entities.Gym;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Gym;

public sealed class GymService : IGymService
{
    private readonly IGymRepository _gymRepository;
    private readonly ITrainingRepository _trainingRepository;
    private readonly IUnitOfWork _unitOfWork;

    public GymService(IGymRepository gymRepository, ITrainingRepository trainingRepository, IUnitOfWork unitOfWork)
    {
        _gymRepository = gymRepository;
        _trainingRepository = trainingRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> AddGymAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.User> routeUserId, string name, string? address, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || routeUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidGymError(Messages.InvalidId));
        }

        if (currentUser.Id != routeUserId)
        {
            return Result<Unit, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Unit, AppError>.Failure(new InvalidGymError(Messages.FieldRequired));
        }

        Id<Address>? addressId = null;
        if (!string.IsNullOrWhiteSpace(address) && Id<Address>.TryParse(address, out var parsedAddressId))
        {
            addressId = parsedAddressId;
        }

        var gym = new GymEntity
        {
            Id = Id<LgymApi.Domain.Entities.Gym>.New(),
            UserId = currentUser.Id,
            Name = name,
            AddressId = addressId,
            IsDeleted = false
        };

        await _gymRepository.AddAsync(gym, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> DeleteGymAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new InvalidGymError(Messages.InvalidId));
        }

        if (gymId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidGymError(Messages.FieldRequired));
        }

        var gym = await _gymRepository.FindByIdAsync(gymId, cancellationToken);
        if (gym == null)
        {
            return Result<Unit, AppError>.Failure(new GymNotFoundError(Messages.DidntFind));
        }

        if (gym.UserId != currentUser.Id)
        {
            return Result<Unit, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        gym.IsDeleted = true;
        await _gymRepository.UpdateAsync(gym, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<GymListContext, AppError>> GetGymsAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.User> routeUserId, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || routeUserId.IsEmpty)
        {
            return Result<GymListContext, AppError>.Failure(new InvalidGymError(Messages.InvalidId));
        }

        if (currentUser.Id != routeUserId)
        {
            return Result<GymListContext, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        var gyms = await _gymRepository.GetByUserIdAsync(currentUser.Id, cancellationToken);
        var gymIds = gyms.Select(g => g.Id).ToList();
        var trainings = await _trainingRepository.GetByGymIdsAsync(gymIds, cancellationToken);
        var lastTrainings = trainings
            .GroupBy(t => t.GymId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).FirstOrDefault())
            .Where(t => t != null)
            .ToDictionary(t => t!.GymId, t => t!);

        return Result<GymListContext, AppError>.Success(new GymListContext
        {
            Gyms = gyms,
            LastTrainings = lastTrainings
        });
    }

    public async Task<Result<GymEntity, AppError>> GetGymAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<GymEntity, AppError>.Failure(new InvalidGymError(Messages.InvalidId));
        }

        if (gymId.IsEmpty)
        {
            return Result<GymEntity, AppError>.Failure(new InvalidGymError(Messages.FieldRequired));
        }

        var gym = await _gymRepository.FindByIdAsync(gymId, cancellationToken);
        if (gym == null)
        {
            return Result<GymEntity, AppError>.Failure(new GymNotFoundError(Messages.DidntFind));
        }

        if (gym.UserId != currentUser.Id)
        {
            return Result<GymEntity, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        return Result<GymEntity, AppError>.Success(gym);
    }

    public async Task<Result<Unit, AppError>> UpdateGymAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, string name, string? address, CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new InvalidGymError(Messages.InvalidId));
        }

        if (gymId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidGymError(Messages.FieldRequired));
        }

        var gym = await _gymRepository.FindByIdAsync(gymId, cancellationToken);
        if (gym == null)
        {
            return Result<Unit, AppError>.Failure(new GymNotFoundError(Messages.DidntFind));
        }

        if (gym.UserId != currentUser.Id)
        {
            return Result<Unit, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        gym.Name = name;
        if (!string.IsNullOrWhiteSpace(address) && Id<Address>.TryParse(address, out var addressId))
        {
            gym.AddressId = addressId;
        }

        await _gymRepository.UpdateAsync(gym, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
