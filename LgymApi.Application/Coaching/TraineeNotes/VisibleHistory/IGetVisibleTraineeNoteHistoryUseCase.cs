using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.TraineeNotes.Models;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleHistory;

internal interface IGetVisibleTraineeNoteHistoryUseCase
{
    Task<Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>> ExecuteAsync(
        GetVisibleTraineeNoteHistoryQuery query,
        CancellationToken cancellationToken = default);
}
