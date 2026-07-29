using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.ExerciseScores.Models;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.ExerciseScores;

public interface IExerciseScoresService
{
    Task<Result<List<ExerciseScoresChartData>, AppError>> GetExerciseScoresChartDataAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
}
