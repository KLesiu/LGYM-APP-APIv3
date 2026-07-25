using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.Relationships.UnlinkTrainee;

public interface IUnlinkTraineeUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(UnlinkTraineeCommand command, CancellationToken cancellationToken = default);
}
