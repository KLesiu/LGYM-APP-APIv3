using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.PlanDay.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using PlanDayEntity = LgymApi.Domain.Entities.PlanDay;
using PlanDayExerciseEntity = LgymApi.Domain.Entities.PlanDayExercise;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.PlanDay;

public sealed partial class PlanDayService : IPlanDayService
{
    public async Task<Result<Unit, AppError>> CreatePlanDayAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Plan> planId, string name, IReadOnlyCollection<PlanDayExerciseInput> exercises, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || planId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var plan = await _planRepository.FindByIdAsync(planId, cancellationToken);
        if (plan == null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(currentUser, plan.UserId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        if (string.IsNullOrWhiteSpace(name) || exercises.Count == 0)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.FieldRequired));
        }

        var planDay = new PlanDayEntity
        {
            Id = Id<PlanDayEntity>.New(),
            PlanId = plan.Id,
            Name = name,
            IsDeleted = false
        };

        await _planDayRepository.AddAsync(planDay, cancellationToken);

        var exercisesToAdd = new List<PlanDayExerciseEntity>();
        var order = 0;
        foreach (var exercise in exercises)
        {
            if (exercise.ExerciseId.IsEmpty)
            {
                continue;
            }

            exercisesToAdd.Add(new PlanDayExerciseEntity
            {
                Id = Id<PlanDayExerciseEntity>.New(),
                PlanDayId = planDay.Id,
                ExerciseId = exercise.ExerciseId,
                Order = order++,
                Series = exercise.Series,
                Reps = exercise.Reps
            });
        }

        if (exercisesToAdd.Count > 0)
        {
            await _planDayExerciseRepository.AddRangeAsync(exercisesToAdd, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UpdatePlanDayAsync(UserEntity currentUser, Id<PlanDayEntity> planDayId, string name, IReadOnlyCollection<PlanDayExerciseInput> exercises, CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (string.IsNullOrWhiteSpace(name) || exercises.Count == 0)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.FieldRequired));
        }

        if (planDayId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.DidntFind));
        }

        var planDay = await _planDayRepository.FindByIdAsync(planDayId, cancellationToken);
        if (planDay == null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var plan = await _planRepository.FindByIdAsync(planDay.PlanId, cancellationToken);
        if (plan == null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(currentUser, plan.UserId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            planDay.Name = name;
            await _planDayRepository.UpdateAsync(planDay, cancellationToken);

            await _planDayExerciseRepository.RemoveByPlanDayIdAsync(planDay.Id, cancellationToken);

            var exercisesToAdd = new List<PlanDayExerciseEntity>();
            var order = 0;
            foreach (var exercise in exercises)
            {
                if (exercise.ExerciseId.IsEmpty)
                {
                    continue;
                }

                exercisesToAdd.Add(new PlanDayExerciseEntity
                {
                    Id = Id<PlanDayExerciseEntity>.New(),
                    PlanDayId = planDay.Id,
                    ExerciseId = exercise.ExerciseId,
                    Order = order++,
                    Series = exercise.Series,
                    Reps = exercise.Reps
                });
            }

            if (exercisesToAdd.Count > 0)
            {
                await _planDayExerciseRepository.AddRangeAsync(exercisesToAdd, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<Unit, AppError>.Success(Unit.Value);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Result<Unit, AppError>> DeletePlanDayAsync(UserEntity currentUser, Id<PlanDayEntity> planDayId, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || planDayId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var planDay = await _planDayRepository.FindByIdAsync(planDayId, cancellationToken);
        if (planDay == null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var plan = await _planRepository.FindByIdAsync(planDay.PlanId, cancellationToken);
        if (plan == null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(currentUser, plan.UserId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        await _planDayRepository.MarkDeletedAsync(planDay.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
