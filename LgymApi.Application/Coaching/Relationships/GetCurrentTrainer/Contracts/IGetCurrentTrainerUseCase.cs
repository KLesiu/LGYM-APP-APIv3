using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;

public interface IGetCurrentTrainerUseCase
{
    Task<Result<CurrentTrainerReadModel, AppError>> ExecuteAsync(
        GetCurrentTrainerQuery query,
        CancellationToken cancellationToken = default);
}
