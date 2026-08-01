using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.Progress.TrainingDates;

public interface IGetTrainingDatesUseCase
{
    Task<Result<List<DateTime>, AppError>> ExecuteAsync(
        GetTrainingDatesQuery query,
        CancellationToken cancellationToken = default);
}
