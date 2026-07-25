using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;

namespace LgymApi.Application.Coaching.Progress.MainRecordsHistory;

public interface IGetMainRecordsHistoryUseCase
{
    Task<Result<List<MainRecordReadModel>, AppError>> ExecuteAsync(
        GetMainRecordsHistoryQuery query,
        CancellationToken cancellationToken = default);
}
