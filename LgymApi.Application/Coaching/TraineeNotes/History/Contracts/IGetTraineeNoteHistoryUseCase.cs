using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.TraineeNotes.History;

public interface IGetTraineeNoteHistoryUseCase
{
    Task<Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>> ExecuteAsync(
        GetTraineeNoteHistoryQuery query,
        CancellationToken cancellationToken = default);
}
