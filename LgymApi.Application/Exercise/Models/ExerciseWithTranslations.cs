using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Exercise.Models;

public sealed class ExerciseWithTranslations
{
    public ProgressExerciseReadModel Exercise { get; init; } = null!;
    public Dictionary<Id<ExerciseEntity>, string> Translations { get; init; } = new();
}
