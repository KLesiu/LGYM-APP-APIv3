using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.TraineeNotes.Create;

public interface ICreateTraineeNoteUseCase
{
    Task<Result<TraineeNoteReadModel, AppError>> ExecuteAsync(
        CreateTraineeNoteCommand command,
        CancellationToken cancellationToken = default);
}
