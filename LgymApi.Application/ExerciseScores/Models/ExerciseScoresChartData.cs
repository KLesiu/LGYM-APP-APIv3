using LgymApi.Domain.ValueObjects;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

namespace LgymApi.Application.Features.ExerciseScores.Models;

public sealed class ExerciseScoresChartData
{
    public string Id { get; init; } = string.Empty;
    public double Value { get; init; }
    public string Date { get; init; } = string.Empty;
    public string ExerciseName { get; init; } = string.Empty;
    public Id<ExerciseEntity> ExerciseId { get; init; }
}
