using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Gym.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Resources;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using GymEntity = LgymApi.Domain.Entities.Gym;

namespace LgymApi.Application.Features.Gym;

public sealed class GymService : IGymService
{
    private readonly IWorkoutGymPersistence _gymRepository;
    private readonly IWorkoutTrainingPersistence _trainingRepository;
    private readonly IPlanDayReferenceReadService _planDayReferences;
    private readonly IUnitOfWork _unitOfWork;

    public GymService(
        IWorkoutGymPersistence gymRepository,
        IWorkoutTrainingPersistence trainingRepository,
        IPlanDayReferenceReadService planDayReferences,
        IUnitOfWork unitOfWork)
    {
        _gymRepository = gymRepository;
        _trainingRepository = trainingRepository;
        _planDayReferences = planDayReferences;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> AddGymAsync(AuthenticatedAccountContext? currentUser, Id<AccountReference> routeUserId, string name, string? address, CancellationToken cancellationToken = default)
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

        Id<LgymApi.Domain.Entities.Address>? addressId = null;
        if (!string.IsNullOrWhiteSpace(address) && Id<LgymApi.Domain.Entities.Address>.TryParse(address, out var parsedAddressId))
        {
            addressId = parsedAddressId;
        }

        var gym = new WorkoutGymWriteModel(Id<LgymApi.Domain.Entities.Gym>.New(), currentUser.Id, name, addressId, false);

        await _gymRepository.AddAsync(gym, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> DeleteGymAsync(AuthenticatedAccountContext? currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, CancellationToken cancellationToken = default)
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

        if (gym.OwnerId != currentUser.Id)
        {
            return Result<Unit, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        await _gymRepository.UpdateAsync(new WorkoutGymWriteModel(gym.Id, gym.OwnerId, gym.Name, gym.AddressId, true), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<GymListContext, AppError>> GetGymsAsync(AuthenticatedAccountContext? currentUser, Id<AccountReference> routeUserId, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || routeUserId.IsEmpty)
        {
            return Result<GymListContext, AppError>.Failure(new InvalidGymError(Messages.InvalidId));
        }

        if (currentUser.Id != routeUserId)
        {
            return Result<GymListContext, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        var gyms = await _gymRepository.GetByAccountIdAsync(currentUser.Id, cancellationToken);
        var gymIds = gyms.Select(g => g.Id).ToList();
        var trainings = await _trainingRepository.GetByGymIdsAsync(gymIds, cancellationToken);
        var lastTrainings = trainings
            .GroupBy(t => t.GymId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).FirstOrDefault())
            .Where(t => t != null)
            .ToDictionary(t => t!.GymId, t => t!);
        var planDays = await _planDayReferences.GetByIdsAsync(
            lastTrainings.Values.Select(training => training.TypePlanDayId).Distinct().ToList(),
            cancellationToken);

        return Result<GymListContext, AppError>.Success(new GymListContext
        {
            Gyms = gyms.ToList(),
            LastTrainings = lastTrainings,
            PlanDays = planDays.ToDictionary(planDay => planDay.PlanDayId)
        });
    }

    public async Task<Result<WorkoutGymPersistenceModel, AppError>> GetGymAsync(AuthenticatedAccountContext? currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<WorkoutGymPersistenceModel, AppError>.Failure(new InvalidGymError(Messages.InvalidId));
        }

        if (gymId.IsEmpty)
        {
            return Result<WorkoutGymPersistenceModel, AppError>.Failure(new InvalidGymError(Messages.FieldRequired));
        }

        var gym = await _gymRepository.FindByIdAsync(gymId, cancellationToken);
        if (gym == null)
        {
            return Result<WorkoutGymPersistenceModel, AppError>.Failure(new GymNotFoundError(Messages.DidntFind));
        }

        if (gym.OwnerId != currentUser.Id)
        {
            return Result<WorkoutGymPersistenceModel, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        return Result<WorkoutGymPersistenceModel, AppError>.Success(gym);
    }

    public async Task<Result<Unit, AppError>> UpdateGymAsync(AuthenticatedAccountContext? currentUser, Id<LgymApi.Domain.Entities.Gym> gymId, string name, string? address, CancellationToken cancellationToken = default)
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

        if (gym.OwnerId != currentUser.Id)
        {
            return Result<Unit, AppError>.Failure(new GymForbiddenError(Messages.Forbidden));
        }

        var addressId = gym.AddressId;
        if (!string.IsNullOrWhiteSpace(address) && Id<LgymApi.Domain.Entities.Address>.TryParse(address, out var parsedAddressId))
        {
            addressId = parsedAddressId;
        }

        await _gymRepository.UpdateAsync(new WorkoutGymWriteModel(gym.Id, gym.OwnerId, name, addressId, gym.IsDeleted), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
