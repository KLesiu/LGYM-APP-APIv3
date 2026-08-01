using LgymApi.Domain.ValueObjects;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.Progress.ExerciseScoresChart;

public sealed record GetExerciseScoresChartQuery(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<ExerciseEntity> ExerciseId);
