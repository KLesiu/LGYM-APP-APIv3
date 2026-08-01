using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.Relationships.DetachFromTrainer;

public interface IDetachFromTrainerUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(DetachFromTrainerCommand command, CancellationToken cancellationToken = default);
}
