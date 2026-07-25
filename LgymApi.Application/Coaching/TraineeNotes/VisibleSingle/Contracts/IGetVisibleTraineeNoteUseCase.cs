using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;

public interface IGetVisibleTraineeNoteUseCase
{
    Task<Result<TraineeNoteReadModel, AppError>> ExecuteAsync(
        GetVisibleTraineeNoteQuery query,
        CancellationToken cancellationToken = default);
}
