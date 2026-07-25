using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.PlanDay.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using PlanDayEntity = LgymApi.Domain.Entities.PlanDay;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.PlanDay;

public sealed partial class PlanDayService : IPlanDayService
{
    public async Task<Result<PlanDayDetailsContext, AppError>> GetPlanDayAsync(UserEntity currentUser, Id<PlanDayEntity> planDayId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || planDayId.IsEmpty)
        {
            return Result<PlanDayDetailsContext, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var planDay = await _planDayRepository.FindByIdAsync(planDayId, cancellationToken);
        if (planDay == null)
        {
            return Result<PlanDayDetailsContext, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var plan = await _planRepository.FindByIdAsync(planDay.PlanId, cancellationToken);
        if (plan == null)
        {
            return Result<PlanDayDetailsContext, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(currentUser, plan.UserId, cancellationToken))
        {
            return Result<PlanDayDetailsContext, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        var exercises = await _planDayExerciseRepository.GetByPlanDayIdAsync(planDay.Id, cancellationToken);
        var exerciseIds = exercises.Select(e => e.ExerciseId).Distinct().ToList();
        var exerciseList = await _exerciseRepository.GetByIdsAsync(exerciseIds, cancellationToken);
        var exerciseMap = exerciseList.ToDictionary(e => e.Id, e => e);
        var globalExerciseIds = exerciseList
            .Where(e => e.UserId == null)
            .Select(e => e.Id)
            .ToList();
        var translations = await _exerciseRepository.GetTranslationsAsync(globalExerciseIds, cultures, cancellationToken);

        return Result<PlanDayDetailsContext, AppError>.Success(new PlanDayDetailsContext
        {
            PlanDay = planDay,
            Exercises = exercises,
            ExerciseMap = exerciseMap,
            Translations = translations
        });
    }

    public async Task<Result<PlanDaysContext, AppError>> GetPlanDaysAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Plan> planId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || planId.IsEmpty)
        {
            return Result<PlanDaysContext, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var plan = await _planRepository.FindByIdAsync(planId, cancellationToken);
        if (plan == null)
        {
            return Result<PlanDaysContext, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(currentUser, plan.UserId, cancellationToken))
        {
            return Result<PlanDaysContext, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        var planDays = await _planDayRepository.GetByPlanIdAsync(plan.Id, cancellationToken);
        if (planDays.Count == 0)
        {
            return Result<PlanDaysContext, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var planDayIds = planDays.Select(pd => pd.Id).ToList();
        var planDayExercises = await _planDayExerciseRepository.GetByPlanDayIdsAsync(planDayIds, cancellationToken);

        var exerciseIds = planDayExercises.Select(e => e.ExerciseId).Distinct().ToList();
        var exerciseList = await _exerciseRepository.GetByIdsAsync(exerciseIds, cancellationToken);
        var exerciseMap = exerciseList.ToDictionary(e => e.Id, e => e);
        var globalExerciseIds = exerciseList
            .Where(e => e.UserId == null)
            .Select(e => e.Id)
            .ToList();
        var translations = await _exerciseRepository.GetTranslationsAsync(globalExerciseIds, cultures, cancellationToken);

        return Result<PlanDaysContext, AppError>.Success(new PlanDaysContext
        {
            PlanDays = planDays,
            PlanDayExercises = planDayExercises,
            ExerciseMap = exerciseMap,
            Translations = translations
        });
    }

    public async Task<Result<List<PlanDayEntity>, AppError>> GetPlanDaysTypesAsync(UserEntity currentUser, Id<UserEntity> routeUserId, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || routeUserId.IsEmpty)
        {
            return Result<List<PlanDayEntity>, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        if (currentUser.Id != routeUserId)
        {
            return Result<List<PlanDayEntity>, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        var plan = await _planRepository.FindActiveByUserIdAsync(currentUser.Id, cancellationToken);
        if (plan == null)
        {
            return Result<List<PlanDayEntity>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var planDays = await _planDayRepository.GetByPlanIdAsync(plan.Id, cancellationToken);
        return Result<List<PlanDayEntity>, AppError>.Success(planDays);
    }

    public async Task<Result<PlanDaysInfoContext, AppError>> GetPlanDaysInfoAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Plan> planId, CancellationToken cancellationToken = default)
    {
        if (currentUser == null || planId.IsEmpty)
        {
            return Result<PlanDaysInfoContext, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var plan = await _planRepository.FindByIdAsync(planId, cancellationToken);
        if (plan == null)
        {
            return Result<PlanDaysInfoContext, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(currentUser, plan.UserId, cancellationToken))
        {
            return Result<PlanDaysInfoContext, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        var planDays = await _planDayRepository.GetByPlanIdAsync(plan.Id, cancellationToken);

        var planDayIds = planDays.Select(pd => pd.Id).ToList();
        var planDayExercises = await _planDayExerciseRepository.GetByPlanDayIdsAsync(planDayIds, cancellationToken);

        var trainings = await _trainingRepository.GetByPlanDayIdsAsync(planDayIds, cancellationToken);
        var lastTrainingMap = trainings
            .GroupBy(t => t.TypePlanDayId)
            .ToDictionary(g => g.Key, g => (DateTime?)g.Max(t => t.CreatedAt).UtcDateTime);

        return Result<PlanDaysInfoContext, AppError>.Success(new PlanDaysInfoContext
        {
            PlanDays = planDays,
            PlanDayExercises = planDayExercises,
            LastTrainingMap = lastTrainingMap
        });
    }
}
