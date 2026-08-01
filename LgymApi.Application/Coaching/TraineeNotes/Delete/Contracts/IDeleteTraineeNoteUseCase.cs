using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.TraineeNotes.Delete;

public interface IDeleteTraineeNoteUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeNoteCommand command,
        CancellationToken cancellationToken = default);
}
