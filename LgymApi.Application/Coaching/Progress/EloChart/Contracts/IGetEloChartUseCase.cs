using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;

namespace LgymApi.Application.Coaching.Progress.EloChart;

public interface IGetEloChartUseCase
{
    Task<Result<List<EloChartPoint>, AppError>> ExecuteAsync(
        GetEloChartQuery query,
        CancellationToken cancellationToken = default);
}
