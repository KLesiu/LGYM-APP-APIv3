using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

internal sealed class TrainingHistoryReadService : ITrainingHistoryReadService
{
    private readonly IAccountAccessReader _accountAccess;
    private readonly IWorkoutTrainingPersistence _trainingRepository;
    private readonly IWorkoutExerciseScorePersistence _exerciseScoreRepository;
    private readonly IPlanDayReferenceReadService _planDayReferences;

    public TrainingHistoryReadService(
        IAccountAccessReader accountAccess,
        IWorkoutTrainingPersistence trainingRepository,
        IWorkoutExerciseScorePersistence exerciseScoreRepository,
        IPlanDayReferenceReadService planDayReferences)
    {
        _accountAccess = accountAccess;
        _trainingRepository = trainingRepository;
        _exerciseScoreRepository = exerciseScoreRepository;
        _planDayReferences = planDayReferences;
    }

    public async Task<Result<WorkoutTrainingReadModel, AppError>> GetLastTrainingAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<WorkoutTrainingReadModel, AppError>.Failure(new InvalidTrainingDataError(Messages.InvalidId));
        }

        if (await _accountAccess.GetByIdAsync(userId, cancellationToken) is null)
        {
            return Result<WorkoutTrainingReadModel, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var training = await _trainingRepository.GetLastByAccountIdAsync(userId, cancellationToken);
        if (training is null)
        {
            return Result<WorkoutTrainingReadModel, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var planDay = await _planDayReferences.GetByIdAsync(training.TypePlanDayId, cancellationToken);
        return Result<WorkoutTrainingReadModel, AppError>.Success(MapTraining(training, planDay));
    }

    public async Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(Id<AccountReference> userId, DateTime createdAt, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<List<TrainingByDateDetails>, AppError>.Failure(new InvalidTrainingDataError(Messages.InvalidId));
        }

        if (await _accountAccess.GetByIdAsync(userId, cancellationToken) is null)
        {
            return Result<List<TrainingByDateDetails>, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var startOfDay = new DateTimeOffset(DateTime.SpecifyKind(createdAt.Date, DateTimeKind.Utc));
        var endOfDay = startOfDay.AddDays(1).AddTicks(-1);
        var trainings = await _trainingRepository.GetByAccountIdAndDateAsync(userId, startOfDay, endOfDay, cancellationToken);
        if (trainings.Count == 0)
        {
            return Result<List<TrainingByDateDetails>, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var trainingScoreRefs = await _trainingRepository.GetExerciseScoreLinksAsync(trainings.Select(training => training.Id).ToList(), cancellationToken);
        var scores = await _exerciseScoreRepository.GetByIdsAsync(trainingScoreRefs.Select(reference => reference.ExerciseScoreId).Distinct().ToList(), cancellationToken);
        var planDays = await _planDayReferences.GetByIdsAsync(trainings.Select(training => training.TypePlanDayId).ToList(), cancellationToken);
        var scoreMap = scores.ToDictionary(score => score.Id, score => score);
        var planDaysById = planDays.ToDictionary(planDay => planDay.PlanDayId);
        var result = new List<TrainingByDateDetails>();
        foreach (var training in trainings)
        {
            var grouped = new Dictionary<Id<Exercise>, EnrichedExercise>();
            var exerciseOrderMap = new Dictionary<Id<Exercise>, int>();
            foreach (var reference in trainingScoreRefs.Where(reference => reference.TrainingId == training.Id))
            {
                if (!scoreMap.TryGetValue(reference.ExerciseScoreId, out var score) || score.Exercise == null)
                {
                    continue;
                }

                if (!grouped.TryGetValue(score.ExerciseId, out var group))
                {
                    group = new EnrichedExercise
                    {
                        ExerciseScoreId = reference.ExerciseScoreId,
                        ExerciseDetails = MapExercise(score.Exercise),
                        ScoresDetails = new List<WorkoutExerciseScoreReadModel>()
                    };
                    grouped[score.ExerciseId] = group;
                    exerciseOrderMap[score.ExerciseId] = reference.Order;
                }
                else
                {
                    exerciseOrderMap[score.ExerciseId] = Math.Min(exerciseOrderMap[score.ExerciseId], reference.Order);
                }

                group.ScoresDetails.Add(MapScore(score));
            }

            result.Add(new TrainingByDateDetails
            {
                Id = training.Id,
                TypePlanDayId = training.TypePlanDayId,
                CreatedAt = training.CreatedAt.UtcDateTime,
                PlanDay = planDaysById.TryGetValue(training.TypePlanDayId, out var planDay) && planDay.Exists && !planDay.IsDeleted
                    ? planDay
                    : null,
                Gym = training.Gym?.Name,
                Exercises = grouped.Values
                    .OrderBy(exercise => exerciseOrderMap[exercise.ExerciseDetails.Id])
                    .Select(exercise => new EnrichedExercise
                    {
                        ExerciseScoreId = exercise.ExerciseScoreId,
                        ExerciseDetails = exercise.ExerciseDetails,
                        ScoresDetails = exercise.ScoresDetails.OrderBy(score => score.Series).ToList()
                    })
                    .ToList()
            });
        }

        return Result<List<TrainingByDateDetails>, AppError>.Success(result);
    }

    public async Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<List<DateTime>, AppError>.Failure(new InvalidTrainingDataError(Messages.InvalidId));
        }

        var trainings = await _trainingRepository.GetDatesByAccountIdAsync(userId, cancellationToken);
        return trainings.Count == 0
            ? Result<List<DateTime>, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind))
            : Result<List<DateTime>, AppError>.Success(trainings.Select(training => training.UtcDateTime).ToList());
    }

    private static WorkoutTrainingReadModel MapTraining(
        WorkoutTrainingPersistenceModel training,
        PlanDayReferenceReadModel? planDay)
        => new(training.Id, training.TypePlanDayId, training.CreatedAt, planDay);

    private static ProgressExerciseReadModel MapExercise(WorkoutExercisePersistenceModel exercise)
        => new(exercise.Id, exercise.Name, exercise.OwnerId, exercise.BodyPart, exercise.EloFormula, exercise.Description, exercise.Image);

    private static WorkoutExerciseScoreReadModel MapScore(WorkoutExerciseScorePersistenceModel score)
        => new(score.Id, score.ExerciseId, score.Weight, score.Unit, score.Reps, score.Series,
            score.Training is null ? null : new WorkoutScoreTrainingReadModel(score.Training.Id, score.Training.GymId, score.Training.Gym?.Name, score.Training.CreatedAt));
}
