using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.TraineeNotes.Update;

public interface IUpdateTraineeNoteUseCase
{
    Task<Result<TraineeNoteReadModel, AppError>> ExecuteAsync(
        UpdateTraineeNoteCommand command,
        CancellationToken cancellationToken = default);
}
