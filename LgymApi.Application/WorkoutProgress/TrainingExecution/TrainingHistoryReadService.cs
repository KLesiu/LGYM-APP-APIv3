using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

internal sealed class TrainingHistoryReadService : ITrainingHistoryReadService
{
    private readonly ITrainingHistoryReadServiceDependencies _dependencies;

    public TrainingHistoryReadService(
        ITrainingHistoryReadServiceDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public async Task<Result<Training, AppError>> GetLastTrainingAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<Training, AppError>.Failure(new InvalidTrainingDataError(Messages.InvalidId));
        }

        if (!await _dependencies.UserAccess.UserExistsAsync(userId, cancellationToken))
        {
            return Result<Training, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var training = await _dependencies.TrainingRepository.GetLastByUserIdAsync(userId, cancellationToken);
        return training == null
            ? Result<Training, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind))
            : Result<Training, AppError>.Success(training);
    }

    public async Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(Id<User> userId, DateTime createdAt, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<List<TrainingByDateDetails>, AppError>.Failure(new InvalidTrainingDataError(Messages.InvalidId));
        }

        if (!await _dependencies.UserAccess.UserExistsAsync(userId, cancellationToken))
        {
            return Result<List<TrainingByDateDetails>, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var startOfDay = new DateTimeOffset(DateTime.SpecifyKind(createdAt.Date, DateTimeKind.Utc));
        var endOfDay = startOfDay.AddDays(1).AddTicks(-1);
        var trainings = await _dependencies.TrainingRepository.GetByUserIdAndDateAsync(userId, startOfDay, endOfDay, cancellationToken);
        if (trainings.Count == 0)
        {
            return Result<List<TrainingByDateDetails>, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var trainingScoreRefs = await _dependencies.TrainingExerciseScoreRepository.GetByTrainingIdsAsync(trainings.Select(training => training.Id).ToList(), cancellationToken);
        var scores = await _dependencies.ExerciseScoreRepository.GetByIdsAsync(trainingScoreRefs.Select(reference => reference.ExerciseScoreId).Distinct().ToList(), cancellationToken);
        var scoreMap = scores.ToDictionary(score => score.Id, score => score);
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
                        ExerciseDetails = score.Exercise,
                        ScoresDetails = new List<ExerciseScore>()
                    };
                    grouped[score.ExerciseId] = group;
                    exerciseOrderMap[score.ExerciseId] = reference.Order;
                }
                else
                {
                    exerciseOrderMap[score.ExerciseId] = Math.Min(exerciseOrderMap[score.ExerciseId], reference.Order);
                }

                group.ScoresDetails.Add(score);
            }

            result.Add(new TrainingByDateDetails
            {
                Id = training.Id,
                TypePlanDayId = training.TypePlanDayId,
                CreatedAt = training.CreatedAt.UtcDateTime,
                PlanDay = training.PlanDay == null
                    ? null
                    : new TrainingPlanDayReadModel(training.PlanDay.Id.ToString(), training.PlanDay.Name),
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

    public async Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<List<DateTime>, AppError>.Failure(new InvalidTrainingDataError(Messages.InvalidId));
        }

        var trainings = await _dependencies.TrainingRepository.GetDatesByUserIdAsync(userId, cancellationToken);
        return trainings.Count == 0
            ? Result<List<DateTime>, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind))
            : Result<List<DateTime>, AppError>.Success(trainings.Select(training => training.UtcDateTime).ToList());
    }
}
