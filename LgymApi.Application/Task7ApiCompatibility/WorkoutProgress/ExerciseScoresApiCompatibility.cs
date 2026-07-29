using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.ExerciseScores;
using LgymApi.Application.Features.ExerciseScores.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;

public interface IExerciseScoresApiCompatibilityService
{
    Task<Result<List<ExerciseScoresChartData>, AppError>> GetExerciseScoresChartDataAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
}

internal sealed class ExerciseScoresApiCompatibilityService : IExerciseScoresApiCompatibilityService
{
    private readonly IExerciseScoresService _exerciseScoresService;

    public ExerciseScoresApiCompatibilityService(IExerciseScoresService exerciseScoresService)
    {
        _exerciseScoresService = exerciseScoresService;
    }

    public Task<Result<List<ExerciseScoresChartData>, AppError>> GetExerciseScoresChartDataAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default)
        => _exerciseScoresService.GetExerciseScoresChartDataAsync(accountId, exerciseId, cancellationToken);
}
